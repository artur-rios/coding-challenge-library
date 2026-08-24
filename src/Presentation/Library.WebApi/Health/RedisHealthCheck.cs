using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace Library.WebApi.Health;

/// <summary>
///     Reports on Redis - as <see cref="HealthStatus.Degraded" />, never as unhealthy.
/// </summary>
/// <remarks>
///     This is the health check that encodes the architecture. Redis is a cache in front of the source
///     of truth, so an instance that cannot reach it can still serve every endpoint correctly, only
///     more slowly. Reporting it unhealthy would make Kubernetes pull every replica out of the load
///     balancer during a Redis blip and take the API down for a dependency it does not need - so the
///     readiness endpoint reports degraded, which is visible to an operator and invisible to the
///     probe.
/// </remarks>
public class RedisHealthCheck(IConnectionMultiplexer connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await connection.GetDatabase().PingAsync();

            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Degraded("Redis is unreachable; the API is serving uncached.", exception);
        }
    }
}
