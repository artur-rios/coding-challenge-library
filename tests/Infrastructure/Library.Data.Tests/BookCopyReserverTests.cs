using Library.Data.Availability;
using Library.Data.Configuration;
using Library.Data.Tests.Support;
using Library.Domain.Entities;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Tests;

// Integration tests for the conditional updates, against a real PostgreSQL. This file is where the
// concurrency claim is actually proven: everything above it - the handlers, the API - is arranged so
// that the correctness of a loan reduces to the correctness of these four statements, and no
// in-memory fake can demonstrate that they are atomic.
[Collection(nameof(DataCollection))]
public class BookCopyReserverTests(LibraryContainers containers)
{
    private async Task<Book> SeedBookAsync(int totalCopies, int? availableCopies = null, bool isActive = true)
    {
        await using var context = containers.CreateContext();

        var book = new Book
        {
            PublicId = Guid.NewGuid(),
            Title = $"Title {Guid.NewGuid():N}",
            Isbn = UniqueIsbn(),
            Author = "Author",
            TotalCopies = totalCopies,
            AvailableCopies = availableCopies ?? totalCopies,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        context.Books.Add(book);

        await context.SaveChangesAsync();

        return book;
    }

    // A valid ISBN-13 per row: the unique index is real here, so two tests reusing one ISBN would
    // collide for reasons that have nothing to do with what they are testing.
    private static string UniqueIsbn()
    {
        var digits = Random.Shared.NextInt64(0, 999_999_999_999L).ToString("D12");

        var checksum = digits.Select((character, index) => (character - '0') * (index % 2 == 0 ? 1 : 3)).Sum();

        return digits + (10 - checksum % 10) % 10;
    }

    private async Task<int> ReadAvailableAsync(long bookId)
    {
        await using var context = containers.CreateContext();

        return await context.Books.Where(book => book.Id == bookId)
            .Select(book => book.AvailableCopies)
            .FirstAsync();
    }

    [IntegrationFact]
    public async Task GivenAvailableCopy_WhenReserving_ThenTheCountDrops()
    {
        // Given
        var book = await SeedBookAsync(totalCopies: 2);

        await using var context = containers.CreateContext();

        // When
        var reserved = await new BookCopyReserver(context).TryReserveCopyAsync(book.Id);

        // Then
        Assert.True(reserved);
        Assert.Equal(1, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenNoCopiesLeft_WhenReserving_ThenItRefusesRatherThanGoingNegative()
    {
        // Given
        var book = await SeedBookAsync(totalCopies: 1, availableCopies: 0);

        await using var context = containers.CreateContext();

        // When
        var reserved = await new BookCopyReserver(context).TryReserveCopyAsync(book.Id);

        // Then - the condition is in the WHERE clause, so the statement matches no row
        Assert.False(reserved);
        Assert.Equal(0, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenInactiveBook_WhenReserving_ThenItRefusesEvenWithCopiesOnTheShelf()
    {
        // Given a withdrawn title with copies present
        var book = await SeedBookAsync(totalCopies: 3, isActive: false);

        await using var context = containers.CreateContext();

        // When
        var reserved = await new BookCopyReserver(context).TryReserveCopyAsync(book.Id);

        // Then - is_active is part of the condition, so a book withdrawn between a handler's check
        // and this statement still cannot be lent
        Assert.False(reserved);
        Assert.Equal(3, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenOneCopyAndTenConcurrentAttempts_WhenReserving_ThenExactlyOneSucceeds()
    {
        // Given a book with a single copy on the shelf
        var book = await SeedBookAsync(totalCopies: 1);

        // When ten independent connections race for it - separate contexts, so this is genuine
        // database-level contention rather than one connection serialising itself
        var attempts = Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var context = containers.CreateContext();

            return await new BookCopyReserver(context).TryReserveCopyAsync(book.Id);
        });

        var results = await Task.WhenAll(attempts);

        // Then - exactly one winner. PostgreSQL takes a row lock for each statement and, at READ
        // COMMITTED, re-evaluates available_copies > 0 against the committed row after waiting for
        // the previous writer - so the losers test their condition against the value the winner
        // wrote, not the value they read on arrival.
        Assert.Equal(1, results.Count(succeeded => succeeded));
        Assert.Equal(9, results.Count(succeeded => !succeeded));

        // Then - and the count is zero, never negative
        Assert.Equal(0, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenThreeCopiesAndTwentyConcurrentAttempts_WhenReserving_ThenExactlyThreeSucceed()
    {
        // Given - the same race with more copies, so the test is about the count rather than about
        // "one winner" happening to fall out of some other mechanism
        var book = await SeedBookAsync(totalCopies: 3);

        // When
        var attempts = Enumerable.Range(0, 20).Select(async _ =>
        {
            await using var context = containers.CreateContext();

            return await new BookCopyReserver(context).TryReserveCopyAsync(book.Id);
        });

        var results = await Task.WhenAll(attempts);

        // Then
        Assert.Equal(3, results.Count(succeeded => succeeded));
        Assert.Equal(0, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenCopyOnLoan_WhenReleasing_ThenTheCountRises()
    {
        // Given
        var book = await SeedBookAsync(totalCopies: 2, availableCopies: 1);

        await using var context = containers.CreateContext();

        // When
        var released = await new BookCopyReserver(context).TryReleaseCopyAsync(book.Id);

        // Then
        Assert.True(released);
        Assert.Equal(2, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenEveryCopyOnTheShelf_WhenReleasing_ThenItRefusesRatherThanInflatingTheStock()
    {
        // Given nothing out on loan
        var book = await SeedBookAsync(totalCopies: 2);

        await using var context = containers.CreateContext();

        // When a release arrives anyway - a double return
        var released = await new BookCopyReserver(context).TryReleaseCopyAsync(book.Id);

        // Then - refused, because a library that believes it owns copies it does not have will keep
        // lending them
        Assert.False(released);
        Assert.Equal(2, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenTwoCopiesOutAndConcurrentReleases_WhenReleasing_ThenTheTotalIsNeverExceeded()
    {
        // Given two copies out of three
        var book = await SeedBookAsync(totalCopies: 3, availableCopies: 1);

        // When five releases race
        var attempts = Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var context = containers.CreateContext();

            return await new BookCopyReserver(context).TryReleaseCopyAsync(book.Id);
        });

        var results = await Task.WhenAll(attempts);

        // Then - two succeed, and the shelf stops at the total
        Assert.Equal(2, results.Count(succeeded => succeeded));
        Assert.Equal(3, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenCopiesOnLoan_WhenRaisingTheTotal_ThenAvailableMovesByTheSameDelta()
    {
        // Given one of three copies out
        var book = await SeedBookAsync(totalCopies: 3, availableCopies: 2);

        await using var context = containers.CreateContext();

        // When two copies are acquired
        var adjusted = await new BookCopyReserver(context).TryAdjustTotalCopiesAsync(book.Id, 5);

        // Then - the copy still out stays out
        Assert.True(adjusted);
        Assert.Equal(4, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenCopiesOnLoan_WhenLoweringTheTotalBelowThem_ThenItIsRefused()
    {
        // Given two of three copies out
        var book = await SeedBookAsync(totalCopies: 3, availableCopies: 1);

        await using var context = containers.CreateContext();

        // When somebody tries to shrink the stock below what readers hold
        var adjusted = await new BookCopyReserver(context).TryAdjustTotalCopiesAsync(book.Id, 1);

        // Then - refused, and nothing moved
        Assert.False(adjusted);
        Assert.Equal(1, await ReadAvailableAsync(book.Id));
    }

    [IntegrationFact]
    public async Task GivenACheckConstraint_WhenWritingANegativeCount_ThenTheDatabaseRefusesIt()
    {
        // Given a book, and a deliberate attempt to corrupt it through EF rather than through the
        // conditional updates - the path a future bug would take
        var book = await SeedBookAsync(totalCopies: 1);

        await using var context = containers.CreateContext();

        var tracked = await context.Books.FirstAsync(entity => entity.Id == book.Id);
        tracked.AvailableCopies = -1;

        // When / Then - the constraint is the backstop under the application's own rules, and it
        // holds even when the code above it does not
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
