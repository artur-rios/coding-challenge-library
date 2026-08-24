using Library.Data.Configuration;
using Library.Data.Loans;
using Library.Data.Relational.Repositories;
using Library.Data.Tests.Support;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Tests;

// Integration tests for the rules the loan table enforces in SQL: the state-machine check
// constraint, the partial unique index over active loans, the restricted foreign key that makes
// history undeletable, and the conditional state transition.
[Collection(nameof(DataCollection))]
public class LoanConstraintTests(LibraryContainers containers)
{
    private static string UniqueIsbn()
    {
        var digits = Random.Shared.NextInt64(0, 999_999_999_999L).ToString("D12");

        var checksum = digits.Select((character, index) => (character - '0') * (index % 2 == 0 ? 1 : 3)).Sum();

        return digits + (10 - checksum % 10) % 10;
    }

    private async Task<(Book book, User user)> SeedAsync(int totalCopies = 3)
    {
        await using var context = containers.CreateContext();

        var book = new Book
        {
            PublicId = Guid.NewGuid(),
            Title = $"Title {Guid.NewGuid():N}",
            Isbn = UniqueIsbn(),
            Author = "Author",
            TotalCopies = totalCopies,
            AvailableCopies = totalCopies,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            PublicId = Guid.NewGuid(),
            Name = "Reader",
            Email = $"reader-{Guid.NewGuid():N}@test.local",
            CreatedAt = DateTime.UtcNow
        };

        context.Books.Add(book);
        context.Users.Add(user);

        await context.SaveChangesAsync();

        return (book, user);
    }

    private async Task<Loan> SeedLoanAsync(Book book, User user, LoanStatus status = LoanStatus.Active)
    {
        await using var context = containers.CreateContext();

        var loan = new Loan
        {
            PublicId = Guid.NewGuid(),
            BookId = book.Id,
            UserId = user.Id,
            Status = status,
            LoanedAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(14),
            ReturnedAt = status == LoanStatus.Returned ? DateTime.UtcNow : null,
            CancelledAt = status == LoanStatus.Cancelled ? DateTime.UtcNow : null
        };

        context.Loans.Add(loan);

        await context.SaveChangesAsync();

        return loan;
    }

    [IntegrationFact]
    public async Task GivenAReaderHoldingATitle_WhenLendingItToThemAgain_ThenTheIndexRefusesIt()
    {
        // Given
        var (book, user) = await SeedAsync();
        await SeedLoanAsync(book, user);

        // When
        var exception = await Assert.ThrowsAnyAsync<DbUpdateException>(() => SeedLoanAsync(book, user));

        // Then - the partial unique index, not the handler's check, is what makes this hold under
        // concurrency
        Assert.True(RelationalErrors.IsUniqueViolation(exception));
    }

    [IntegrationFact]
    public async Task GivenAReaderWhoReturnedATitle_WhenLendingItToThemAgain_ThenItIsAllowed()
    {
        // Given a finished loan
        var (book, user) = await SeedAsync();
        await SeedLoanAsync(book, user, LoanStatus.Returned);

        // When - the same reader borrows the same book again, which is ordinary
        var second = await SeedLoanAsync(book, user);

        // Then - the index is filtered to active loans, so history does not block a new loan
        Assert.NotEqual(Guid.Empty, second.PublicId);
    }

    [IntegrationFact]
    public async Task GivenALoanClaimingToBeReturned_WhenItCarriesNoReturnTimestamp_ThenTheCheckConstraintRefusesIt()
    {
        // Given
        var (book, user) = await SeedAsync();

        await using var context = containers.CreateContext();

        // When - a state and its timestamp disagree, which is what a bug in a transition would look
        // like
        context.Loans.Add(new Loan
        {
            PublicId = Guid.NewGuid(),
            BookId = book.Id,
            UserId = user.Id,
            Status = LoanStatus.Returned,
            LoanedAt = DateTime.UtcNow,
            DueAt = DateTime.UtcNow.AddDays(14),
            ReturnedAt = null
        });

        // Then - the history endpoints would have reported that row without noticing; the constraint
        // is what stops it existing
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [IntegrationFact]
    public async Task GivenABookWithLoans_WhenDeletingIt_ThenTheForeignKeyRefusesToTakeTheHistoryWithIt()
    {
        // Given
        var (book, user) = await SeedAsync();
        await SeedLoanAsync(book, user);

        await using var context = containers.CreateContext();

        var tracked = await context.Books.FirstAsync(entity => entity.Id == book.Id);
        context.Books.Remove(tracked);

        // When / Then - Restrict rather than Cascade, so no code path present or future can erase a
        // loan history by deleting the book it belongs to
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [IntegrationFact]
    public async Task GivenAnActiveLoan_WhenTransitioning_ThenItMovesAndStampsTheRightColumn()
    {
        // Given
        var (book, user) = await SeedAsync();
        var loan = await SeedLoanAsync(book, user);
        var timestamp = DateTime.UtcNow;

        await using var context = containers.CreateContext();

        // When
        var moved = await new LoanTransitionWriter(context)
            .TryTransitionAsync(loan.Id, LoanStatus.Active, LoanStatus.Returned, timestamp);

        // Then
        Assert.True(moved);

        await using var verification = containers.CreateContext();

        var stored = await verification.Loans.FirstAsync(entity => entity.Id == loan.Id);

        Assert.Equal(LoanStatus.Returned, stored.Status);
        Assert.NotNull(stored.ReturnedAt);
        Assert.Null(stored.CancelledAt);
    }

    [IntegrationFact]
    public async Task GivenOneActiveLoanAndConcurrentReturns_WhenTransitioning_ThenExactlyOneSucceeds()
    {
        // Given a loan several requests try to return at once
        var (book, user) = await SeedAsync();
        var loan = await SeedLoanAsync(book, user);

        // When
        var attempts = Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var context = containers.CreateContext();

            return await new LoanTransitionWriter(context).TryTransitionAsync(
                loan.Id, LoanStatus.Active, LoanStatus.Returned, DateTime.UtcNow);
        });

        var results = await Task.WhenAll(attempts);

        // Then - one winner. This is what stops a single loan crediting the shelf with several
        // copies, which would leave the library believing it owns stock it does not have.
        Assert.Equal(1, results.Count(succeeded => succeeded));
    }

    [IntegrationFact]
    public async Task GivenAlreadyReturnedLoan_WhenTransitioningFromActive_ThenItRefuses()
    {
        // Given
        var (book, user) = await SeedAsync();
        var loan = await SeedLoanAsync(book, user, LoanStatus.Returned);

        await using var context = containers.CreateContext();

        // When
        var moved = await new LoanTransitionWriter(context)
            .TryTransitionAsync(loan.Id, LoanStatus.Active, LoanStatus.Cancelled, DateTime.UtcNow);

        // Then - the expected state is part of the WHERE clause, so the update matches no row
        Assert.False(moved);
    }
}
