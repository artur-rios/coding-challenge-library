using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Query.Handlers;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Library.Testing.Attributes;
using Library.Testing.Mock;

namespace Library.Query.Tests;

// Unit tests for the availability read: what it computes, what it caches, and - the part that
// matters - that it is explicitly advisory rather than the authority on whether a loan may happen.
public class GetBookAvailabilityQueryHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static (GetBookAvailabilityQueryHandler handler, AsyncFakeRepository<Book> books,
        AsyncFakeRepository<Loan> loans, FakeCacheStore cache) Build()
    {
        var books = new AsyncFakeRepository<Book>();
        var loans = new AsyncFakeRepository<Loan>();
        var cache = new FakeCacheStore();

        return (new GetBookAvailabilityQueryHandler(books, loans, cache, new FakeTimeProvider(Now)),
            books, loans, cache);
    }

    private static async Task<Book> SeedBookAsync(
        AsyncFakeRepository<Book> books, int totalCopies = 3, int? availableCopies = null,
        bool isActive = true)
    {
        var book = new Book
        {
            PublicId = Guid.NewGuid(),
            Title = "Domain-Driven Design",
            Isbn = "9780321125217",
            Author = "Evans",
            TotalCopies = totalCopies,
            AvailableCopies = availableCopies ?? totalCopies,
            IsActive = isActive
        };

        await books.CreateAsync(book);

        return book;
    }

    [UnitFact]
    public async Task GivenBookWithCopiesOut_WhenReadingAvailability_ThenCountsAreReported()
    {
        // Given three copies, one of them out
        var (handler, books, loans, _) = Build();
        var book = await SeedBookAsync(books, totalCopies: 3, availableCopies: 2);
        await loans.CreateAsync(new Loan { BookId = book.Id, Status = LoanStatus.Active });
        await loans.CreateAsync(new Loan { BookId = book.Id, Status = LoanStatus.Returned });

        // When
        var output = await handler.HandleAsync(new GetBookAvailabilityQuery { BookId = book.PublicId });

        // Then - the returned loan is history and does not count against the shelf
        Assert.True(output.Success);
        Assert.Equal(3, output.Data!.TotalCopies);
        Assert.Equal(2, output.Data.AvailableCopies);
        Assert.Equal(1, output.Data.ActiveLoans);
        Assert.True(output.Data.CanBeLoaned);
    }

    [UnitFact]
    public async Task GivenInactiveBookWithCopiesOnTheShelf_WhenReadingAvailability_ThenItCannotBeLoaned()
    {
        // Given a withdrawn title whose copies are all present
        var (handler, books, _, _) = Build();
        var book = await SeedBookAsync(books, totalCopies: 3, isActive: false);

        // When
        var output = await handler.HandleAsync(new GetBookAvailabilityQuery { BookId = book.PublicId });

        // Then - copies on the shelf are not the same thing as a book that can be borrowed
        Assert.Equal(3, output.Data!.AvailableCopies);
        Assert.False(output.Data.CanBeLoaned);
    }

    [UnitFact]
    public async Task GivenNoCopiesLeft_WhenReadingAvailability_ThenItCannotBeLoaned()
    {
        // Given
        var (handler, books, _, _) = Build();
        var book = await SeedBookAsync(books, totalCopies: 1, availableCopies: 0);

        // When
        var output = await handler.HandleAsync(new GetBookAvailabilityQuery { BookId = book.PublicId });

        // Then
        Assert.False(output.Data!.CanBeLoaned);
    }

    [UnitFact]
    public async Task GivenAMiss_WhenReadingAvailability_ThenTheAnswerIsCached()
    {
        // Given
        var (handler, books, _, cache) = Build();
        var book = await SeedBookAsync(books);

        // When
        await handler.HandleAsync(new GetBookAvailabilityQuery { BookId = book.PublicId });

        // Then
        Assert.Contains(CacheKeys.Availability(book.PublicId), cache.WrittenKeys);
    }

    [UnitFact]
    public async Task GivenACachedAnswer_WhenReadingAvailability_ThenItIsServedWithoutTouchingTheDatabase()
    {
        // Given a cached figure that deliberately disagrees with the database, so the source of the
        // answer is unambiguous
        var (handler, books, _, cache) = Build();
        var book = await SeedBookAsync(books, totalCopies: 3, availableCopies: 3);

        cache.Seed(CacheKeys.Availability(book.PublicId), new BookAvailabilityOutput
        {
            BookId = book.PublicId,
            Title = "From the cache",
            TotalCopies = 3,
            AvailableCopies = 1,
            ActiveLoans = 2,
            IsActive = true,
            CalculatedAt = Now.UtcDateTime.AddSeconds(-10)
        });

        // When
        var output = await handler.HandleAsync(new GetBookAvailabilityQuery { BookId = book.PublicId });

        // Then - the cached value wins, which is exactly why this figure is advisory and why the loan
        // handler never consults it
        Assert.Equal("From the cache", output.Data!.Title);
        Assert.Equal(1, output.Data.AvailableCopies);
    }

    [UnitFact]
    public async Task GivenUnknownBook_WhenReadingAvailability_ThenNotFoundIsReportedAndNothingIsCached()
    {
        // Given
        var (handler, _, _, cache) = Build();

        // When
        var output = await handler.HandleAsync(new GetBookAvailabilityQuery { BookId = Guid.NewGuid() });

        // Then - absence is not cached: it would need its own invalidation on book creation, and the
        // failure mode is a caller being told the book they just created does not exist
        Assert.False(output.Success);
        Assert.Contains(BookMessages.BookNotFound, output.Errors);
        Assert.Empty(cache.WrittenKeys);
    }

    [UnitFact]
    public async Task GivenAFreshRead_WhenReadingAvailability_ThenTheAnswerIsStampedWithItsTime()
    {
        // Given
        var (handler, books, _, _) = Build();
        var book = await SeedBookAsync(books);

        // When
        var output = await handler.HandleAsync(new GetBookAvailabilityQuery { BookId = book.PublicId });

        // Then - so a caller can see the age of a cached answer instead of assuming it is current
        Assert.Equal(Now.UtcDateTime, output.Data!.CalculatedAt);
    }
}
