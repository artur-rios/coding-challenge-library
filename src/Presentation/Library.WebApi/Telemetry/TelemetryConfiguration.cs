using Library.Shared.Telemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Library.WebApi.Telemetry;

/// <summary>Wires OpenTelemetry traces and metrics.</summary>
public static class TelemetryConfiguration
{
    /// <summary>The service name traces and metrics are attributed to.</summary>
    public const string ServiceName = "library-api";

    /// <summary>
    ///     Registers tracing and metrics, exporting over OTLP when an endpoint is configured.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="otlpEndpoint">
    ///     The collector endpoint, or <c>null</c>. With no endpoint the instrumentation is still
    ///     registered and still collects - it simply exports nowhere, which is what lets the same
    ///     build run locally without a collector and in a cluster with one, without a code path that
    ///     only production exercises.
    /// </param>
    public static IServiceCollection AddLibraryTelemetry(
        this IServiceCollection services, string? otlpEndpoint)
    {
        var resource = ResourceBuilder.CreateDefault()
            .AddService(ServiceName)
            // The environment as a resource attribute, so one collector can hold several deployments
            // of this service and still tell their traces apart.
            .AddAttributes([
                new KeyValuePair<string, object>(
                    "deployment.environment",
                    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown")
            ]);

        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation(options =>
                        // The probes are the highest-volume requests a cluster makes and the least
                        // interesting; tracing them buries the requests that matter.
                        options.Filter = httpContext =>
                            !httpContext.Request.Path.StartsWithSegments("/health"))
                    .AddHttpClientInstrumentation()
                    // Npgsql publishes its own activities, so the SQL each request issued - including
                    // the conditional updates the loan flow turns on - appears inside that request's
                    // trace.
                    .AddSource("Npgsql");

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // The four business counters the challenge asks for.
                    .AddMeter(LibraryMetrics.MeterName);

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(otlpEndpoint));
                }
            });

        return services;
    }
}
