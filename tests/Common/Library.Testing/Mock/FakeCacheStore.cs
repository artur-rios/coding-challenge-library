using Library.Shared.Caching;

namespace Library.Testing.Mock;

/// <summary>
///     A cache that actually stores, so a test can assert both that a read is served from it and that
///     a write drops the right keys.
/// </summary>
public class FakeCacheStore : ICacheStore
{
    private readonly Dictionary<string, object> _entries = [];

    /// <summary>Every key removed, in order, including prefixes passed to the prefix removal.</summary>
    public List<string> RemovedKeys { get; } = [];

    /// <summary>Every key written, in order.</summary>
    public List<string> WrittenKeys { get; } = [];

    /// <summary>The keys currently held.</summary>
    public IReadOnlyCollection<string> Keys => _entries.Keys;

    /// <summary>Puts a value in directly, to set up a cache-hit test.</summary>
    public void Seed<T>(string key, T value) where T : class => _entries[key] = value;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class =>
        Task.FromResult(_entries.TryGetValue(key, out var value) ? value as T : null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
        where T : class
    {
        _entries[key] = value;
        WrittenKeys.Add(key);

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        _entries.Remove(key);
        RemovedKeys.Add(key);

        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        foreach (var key in _entries.Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                     .ToList())
        {
            _entries.Remove(key);
        }

        RemovedKeys.Add(prefix);

        return Task.CompletedTask;
    }
}
