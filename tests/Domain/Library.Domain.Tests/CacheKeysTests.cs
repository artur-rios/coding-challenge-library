using Library.Shared.Caching;
using Library.Testing.Attributes;

namespace Library.Domain.Tests;

// Unit tests for the cache keys. Small, and worth having: a reader and a writer that disagree on a
// key produce a cache that is never invalidated, and nothing else in the system would notice.
public class CacheKeysTests
{
    [UnitFact]
    public void GivenABook_WhenBuildingItsAvailabilityKey_ThenItSitsUnderThePrefixWritersDrop()
    {
        // Given
        var bookId = Guid.NewGuid();

        // When
        var key = CacheKeys.Availability(bookId);

        // Then
        Assert.StartsWith(CacheKeys.AvailabilityPrefix, key);
        Assert.Contains(bookId.ToString(), key);
    }

    [UnitFact]
    public void GivenTwoBooks_WhenBuildingAvailabilityKeys_ThenTheyDoNotCollide()
    {
        Assert.NotEqual(CacheKeys.Availability(Guid.NewGuid()), CacheKeys.Availability(Guid.NewGuid()));
    }

    [UnitFact]
    public void GivenFiltersDifferingOnlyInCase_WhenBuildingCatalogKeys_ThenTheyShareOneEntry()
    {
        // Given the listing compares case-insensitively, so two spellings are the same query
        var first = CacheKeys.Catalog("Pragmatic", null, false, 1, 50);
        var second = CacheKeys.Catalog("pragmatic", null, false, 1, 50);

        // Then
        Assert.Equal(first, second);
    }

    [UnitFact]
    public void GivenDifferentPages_WhenBuildingCatalogKeys_ThenTheyAreDistinct()
    {
        Assert.NotEqual(
            CacheKeys.Catalog(null, null, false, 1, 50),
            CacheKeys.Catalog(null, null, false, 2, 50));
    }

    [UnitFact]
    public void GivenAdjacentFilterSegments_WhenBuildingCatalogKeys_ThenTheyDoNotCollide()
    {
        // "title a, no author" and "no title, author a" must not produce one key; the empty marker in
        // the key builder is what keeps them apart.
        Assert.NotEqual(
            CacheKeys.Catalog("a", null, false, 1, 50),
            CacheKeys.Catalog(null, "a", false, 1, 50));
    }

    [UnitFact]
    public void GivenEveryCatalogKey_WhenBuilt_ThenItSitsUnderThePrefixWritersDrop()
    {
        // The listing is invalidated by prefix, so a key outside the prefix would never be dropped.
        Assert.StartsWith(CacheKeys.CatalogPrefix, CacheKeys.Catalog("t", "a", true, 3, 25));
    }
}
