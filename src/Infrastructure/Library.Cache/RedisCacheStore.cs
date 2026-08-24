using System.Text.Json;
using Library.Shared.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Library.Cache;

/// <summary>
///     Redis implementation of <see cref="ICacheStore" />.
/// </summary>
/// <remarks>
///     <para>
///         Every operation is failure-tolerant, and that is the central decision in this class. Redis
///         is an accelerator in front of PostgreSQL, never the authority: a read that cannot reach it
///         behaves as a miss and goes to the database, and a write that cannot reach it logs and
///         carries on. The alternative - propagating the failure - would mean a Redis outage takes the
///         API down with it, which is a strictly worse system than a slow one.
///     </para>
///     <para>
///         The cost of that choice is a failed invalidation leaving a stale entry behind. It is bounded
///         by the short time to live every entry is written with, and it can only ever affect the two
///         cached reads - the catalogue listing and the advisory availability figure. It cannot affect
///         whether a loan is granted, because that decision never consults this class.
///     </para>
/// </remarks>
public class RedisCacheStore(IConnectionMultiplexer connection, ILogger<RedisCacheStore> logger)
    : ICacheStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
    {
        try
        {
            var value = await connection.GetDatabase().StringGetAsync(key);

            // Cast to string explicitly: RedisValue converts implicitly to both string and
            // ReadOnlySpan<byte>, and Deserialize has an overload for each.
            return value.IsNullOrEmpty
                ? null
                : JsonSerializer.Deserialize<T>((string)value!, SerializerOptions);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Cache read failed for {Key}; falling back to the database", key);

            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        where T : class
    {
        try
        {
            await connection.GetDatabase().StringSetAsync(
                key, JsonSerializer.Serialize(value, SerializerOptions), ttl);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(exception, "Cache write failed for {Key}; the entry was not stored", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await connection.GetDatabase().KeyDeleteAsync(key);
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            // Logged at warning, not debug: this is the failure mode that leaves a stale entry, and
            // an operator correlating "somebody saw an old count" with a Redis blip needs to find it.
            logger.LogWarning(
                exception, "Cache invalidation failed for {Key}; a stale entry may survive its TTL", key);
        }
    }

    /// <inheritdoc />
    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var database = connection.GetDatabase();

            foreach (var endpoint in connection.GetEndPoints())
            {
                var server = connection.GetServer(endpoint);

                // Replicas hold the same keys as their primary and refuse writes; scanning them would
                // repeat the work and then fail on the delete.
                if (server.IsReplica || !server.IsConnected)
                {
                    continue;
                }

                // SCAN, never KEYS. KEYS walks the whole keyspace in one blocking call, which on a
                // shared Redis stalls every other client for as long as it takes; SCAN returns in
                // bounded batches and lets other work interleave.
                await foreach (var key in server.KeysAsync(pattern: $"{prefix}*", pageSize: 250)
                                   .WithCancellation(ct))
                {
                    await database.KeyDeleteAsync(key);
                }
            }
        }
        catch (Exception exception) when (IsCacheFailure(exception))
        {
            logger.LogWarning(
                exception,
                "Cache invalidation failed for prefix {Prefix}; stale entries may survive their TTL",
                prefix);
        }
    }

    // Redis being unreachable, or the stored value no longer matching the type that reads it after a
    // deployment. Both are cache problems and both mean "go to the database"; anything else is a bug
    // in this API and is allowed to propagate.
    private static bool IsCacheFailure(Exception exception) =>
        exception is RedisException or JsonException or TimeoutException;
}
