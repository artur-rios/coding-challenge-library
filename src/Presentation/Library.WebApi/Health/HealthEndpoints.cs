using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Library.WebApi.Health;

/// <summary>Maps the two probe endpoints and writes their JSON body.</summary>
public static class HealthEndpoints
{
    /// <summary>Tag marking a check that gates readiness.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Liveness route.</summary>
    public const string LiveRoute = "/health/live";

    /// <summary>Readiness route.</summary>
    public const string ReadyRoute = "/health/ready";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps <c>/health/live</c> and <c>/health/ready</c>.</summary>
    /// <remarks>
    ///     <para>
    ///         The split is the point. Liveness runs no checks at all - it answers as long as the
    ///         process is up and the pipeline is responsive, which is exactly what a liveness probe
    ///         should ask. Wiring the database into it would mean a PostgreSQL outage causes Kubernetes
    ///         to restart every replica in a loop, which cannot help and actively hinders recovery by
    ///         hammering the database with reconnections as each pod starts.
    ///     </para>
    ///     <para>
    ///         Readiness runs the checks tagged <c>ready</c>: PostgreSQL, which the API genuinely
    ///         cannot serve without, and Redis, which reports degraded rather than unhealthy so it
    ///         cannot take the pod out of rotation.
    ///     </para>
    /// </remarks>
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LiveRoute, new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = WriteResponseAsync
        });

        app.MapHealthChecks(ReadyRoute, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(ReadyTag),
            ResponseWriter = WriteResponseAsync
        });
    }

    private static Task WriteResponseAsync(HttpContext httpContext, HealthReport report)
    {
        httpContext.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                // The description, never the exception: the body says which dependency is unhappy,
                // and the log line the check produced carries the detail.
                description = entry.Value.Description,
                durationMs = entry.Value.Duration.TotalMilliseconds
            })
        };

        return httpContext.Response.WriteAsync(JsonSerializer.Serialize(payload, SerializerOptions));
    }
}
