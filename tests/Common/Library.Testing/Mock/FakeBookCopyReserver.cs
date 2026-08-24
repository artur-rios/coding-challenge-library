using Library.Domain.Entities;
using Library.Shared.Availability;

namespace Library.Testing.Mock;

/// <summary>
///     An in-memory stand-in for the conditional updates, applying the same conditions to the fake
///     repository's book rows.
/// </summary>
/// <remarks>
///     This fake reproduces the <em>rules</em> - no copy is taken when none is free, no copy is
///     returned past the total, a total cannot drop below the copies on loan - so the handlers'
///     branches can be covered without a database. It does not reproduce the <em>atomicity</em>, which
///     is the property that matters under concurrency and which no in-memory fake can demonstrate.
///     That is proven against a real PostgreSQL in the integration and functional suites, and this
///     fake exists so those slow tests are not also carrying the burden of covering ordinary branches.
/// </remarks>
public class FakeBookCopyReserver(AsyncFakeRepository<Book> books) : IBookCopyReserver
{
    /// <summary>How many times a copy was successfully taken.</summary>
    public int Reservations { get; private set; }

    /// <summary>How many times a copy was successfully put back.</summary>
    public int Releases { get; private set; }

    /// <summary>When set, the next reservation refuses regardless of the counts.</summary>
    public bool NextReservationFails { get; set; }

    /// <summary>When set, the next release refuses - the inconsistency branch.</summary>
    public bool NextReleaseFails { get; set; }

    public Task<bool> TryReserveCopyAsync(long bookId, CancellationToken ct = default)
    {
        if (NextReservationFails)
        {
            NextReservationFails = false;

            return Task.FromResult(false);
        }

        var book = Find(bookId);

        if (book is null || !book.IsActive || book.AvailableCopies <= 0)
        {
            return Task.FromResult(false);
        }

        book.AvailableCopies--;
        Reservations++;

        return Task.FromResult(true);
    }

    public Task<bool> TryReleaseCopyAsync(long bookId, CancellationToken ct = default)
    {
        if (NextReleaseFails)
        {
            NextReleaseFails = false;

            return Task.FromResult(false);
        }

        var book = Find(bookId);

        if (book is null || book.AvailableCopies >= book.TotalCopies)
        {
            return Task.FromResult(false);
        }

        book.AvailableCopies++;
        Releases++;

        return Task.FromResult(true);
    }

    public Task<bool> TryAdjustTotalCopiesAsync(
        long bookId, int totalCopies, CancellationToken ct = default)
    {
        var book = Find(bookId);

        if (book is null)
        {
            return Task.FromResult(false);
        }

        var delta = totalCopies - book.TotalCopies;

        if (book.AvailableCopies + delta < 0)
        {
            return Task.FromResult(false);
        }

        book.AvailableCopies += delta;
        book.TotalCopies = totalCopies;

        return Task.FromResult(true);
    }

    private Book? Find(long bookId) => books.Items.FirstOrDefault(book => book.Id == bookId);
}
