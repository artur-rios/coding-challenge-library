using Library.Shared.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Library.Cache.DependencyInjection;

/// <summary>Wires the cache: Redis when it is configured, a no-op store when it is not.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The environment variable the Redis connection string is read from.</summary>
    public const string ConnectionStringVariable = "LIBRARY_REDIS_CONNECTIONSTRING";

    /// <summary>
    ///     Registers <see cref="RedisCacheStore" /> over a shared multiplexer, or
    ///     <see cref="NullCacheStore" /> when no connection string is configured.
    /// </summary>
    /// <remarks>
    ///     One multiplexer for the process, as a singleton, because that is what StackExchange.Redis
    ///     is designed for - it multiplexes every command over a small number of connections, and
    ///     creating one per request would exhaust the connection pool under exactly the load the cache
    ///     exists to absorb.
    /// </remarks>
    public static IServiceCollection AddLibraryCache(
        this IServiceCollection services, string? connectionString = null)
    {
        var resolved = connectionString ?? Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(resolved))
        {
            services.AddSingleton<ICacheStore, NullCacheStore>();

            return services;
        }

        services.AddSingleton<IConnectionMultiplexer>(provider =>
        {
            var options = ConfigurationOptions.Parse(resolved);

            // The API must start whether or not Redis is up: with this false, construction throws
            // when Redis is unreachable and the container never becomes ready - for a dependency that
            // is explicitly optional. Instead the multiplexer reconnects in the background and the
            // store treats the interim as a miss.
            options.AbortOnConnectFail = false;
            options.ConnectRetry = 3;
            options.ClientName = "library-api";

            var logger = provider.GetRequiredService<ILogger<RedisCacheStore>>();

            var multiplexer = ConnectionMultiplexer.Connect(options);

            multiplexer.ConnectionFailed += (_, args) =>
                logger.LogWarning("Redis connection failed ({FailureType})", args.FailureType);

            multiplexer.ConnectionRestored += (_, _) =>
                logger.LogInformation("Redis connection restored");

            return multiplexer;
        });

        services.AddSingleton<ICacheStore, RedisCacheStore>();

        return services;
    }
}
