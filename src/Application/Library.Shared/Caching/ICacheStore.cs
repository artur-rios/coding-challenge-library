namespace Library.Shared.Caching;

/// <summary>
///     The read-through cache the query handlers use, and the invalidation the write handlers
///     perform.
/// </summary>
/// <remarks>
///     <para>
///         Deliberately small, and deliberately not Redis-shaped: the application layer states what it
///         wants cached and for how long, and the infrastructure decides how. That is what lets the
///         unit tests run against an in-memory implementation and the API run against Redis without
///         either side knowing.
///     </para>
///     <para>
///         Every method is allowed to fail quietly. Redis is an accelerator here, never the source of
///         truth: a cache that is down must slow the API, not break it, so implementations swallow
///         connection failures and behave as a miss.
///     </para>
/// </remarks>
public interface ICacheStore
{
    /// <summary>
    ///     Returns the cached value for <paramref name="key" />, or <c>null</c> on a miss - which
    ///     includes the cache being unreachable.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class;

    /// <summary>Stores <paramref name="value" /> under <paramref name="key" /> for <paramref name="ttl" />.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class;

    /// <summary>Drops a single key.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    ///     Drops every key under a prefix, for the entries whose exact key a writer cannot know - the
    ///     catalogue listing, which is cached per filter and page.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
