using Library.Shared.Caching;

namespace Library.Cache;

/// <summary>
///     A cache that stores nothing: every read misses, every write is discarded.
/// </summary>
/// <remarks>
///     Registered when no Redis connection string is configured, so the API still runs - correctly,
///     just without the accelerator - on a machine that has not started Redis. This is only safe
///     because nothing in this codebase depends on the cache for correctness; if that ever stopped
///     being true, this class would have to go rather than be quietly kept.
/// </remarks>
public class NullCacheStore : ICacheStore
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
        Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        where T : class => Task.CompletedTask;

    public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default) => Task.CompletedTask;
}
