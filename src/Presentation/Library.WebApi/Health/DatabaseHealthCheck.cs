using Library.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Library.WebApi.Health;

/// <summary>
///     Readiness check for PostgreSQL: can this instance actually reach the database it needs to serve
///     traffic?
/// </summary>
/// <remarks>
///     <c>CanConnectAsync</c> rather than a query against a table. The question a readiness probe asks
///     is whether to send this pod requests, and a connection is exactly that question; running a real
///     query would add load proportional to the probe interval times the replica count, for an answer
///     that is no more informative.
/// </remarks>
public class DatabaseHealthCheck(AppDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context1, CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
        catch (Exception exception)
        {
            // The exception is attached for the operator reading the endpoint, not returned to an
            // anonymous caller: the readiness endpoint is not published outside the cluster.
            return HealthCheckResult.Unhealthy("PostgreSQL check failed.", exception);
        }
    }
}
