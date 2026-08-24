namespace Library.Shared.Caching;

/// <summary>
///     Every cache key the application uses, built in one place so a writer invalidating an entry and
///     a reader populating it cannot drift apart.
/// </summary>
/// <remarks>
///     The keys are grouped by prefix on purpose. Availability is keyed by book, so a loan of one book
///     drops exactly one key. The catalogue listing is keyed by its whole filter and page, which a
///     writer cannot reconstruct, so it is dropped by prefix instead - coarser, but correct, and the
///     listing is cheap to rebuild.
/// </remarks>
public static class CacheKeys
{
    /// <summary>Prefix for the per-book availability entries.</summary>
    public const string AvailabilityPrefix = "library:availability:";

    /// <summary>Prefix for the catalogue listing entries.</summary>
    public const string CatalogPrefix = "library:catalog:";

    /// <summary>The availability entry of one book.</summary>
    public static string Availability(Guid bookId) => $"{AvailabilityPrefix}{bookId}";

    /// <summary>One page of one filtering of the catalogue listing.</summary>
    public static string Catalog(string? title, string? author, bool includeInactive, int pageNumber, int pageSize) =>
        $"{CatalogPrefix}{Normalize(title)}:{Normalize(author)}:{includeInactive}:{pageNumber}:{pageSize}";

    // Lower-cased so two filters that differ only in case share one entry, and the empty marker keeps
    // "a:b" from colliding with "a" and "b" as adjacent segments.
    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToLowerInvariant();
}
