using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Query.Handlers;
using Library.Query.Input;
using Library.Query.Input.Validation;
using Library.Shared.Messages;
using Library.Testing.Attributes;
using Library.Testing.Mock;

namespace Library.Query.Tests;

// Unit tests for the two history reads. The property under test in both is the same one the whole
// "history is not erased" requirement turns on: returned and cancelled loans are part of the answer,
// not filtered out of it.
public class HistoryQueryHandlerTests
{
    private static readonly DateTime Base = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<(Book book, User user)> SeedPairAsync(
        AsyncFakeRepository<Book> books, AsyncFakeRepository<User> users)
    {
        var book = new Book
        {
            PublicId = Guid.NewGuid(), Title = "Refactoring", Author = "Fowler",
            Isbn = "9780134757599", TotalCopies = 3, AvailableCopies = 3
        };

        var user = new User { PublicId = Guid.NewGuid(), Name = "Ana", Email = "ana@test.local" };

        await books.CreateAsync(book);
        await users.CreateAsync(user);

        return (book, user);
    }

    private static async Task SeedLoanAsync(
        AsyncFakeRepository<Loan> loans, Book book, User user, LoanStatus status, int daysAgo)
    {
        await loans.CreateAsync(new Loan
        {
            PublicId = Guid.NewGuid(),
            BookId = book.Id,
            UserId = user.Id,
            Book = book,
            User = user,
            Status = status,
            LoanedAt = Base.AddDays(-daysAgo),
            DueAt = Base.AddDays(-daysAgo + 14),
            ReturnedAt = status == LoanStatus.Returned ? Base.AddDays(-daysAgo + 3) : null,
            CancelledAt = status == LoanStatus.Cancelled ? Base.AddDays(-daysAgo + 1) : null
        });
    }

    [UnitFact]
    public async Task GivenLoansInEveryState_WhenReadingBookHistory_ThenAllOfThemAreReturned()
    {
        // Given a book lent three times, with only one copy still out
        var books = new AsyncFakeRepository<Book>();
        var users = new AsyncFakeRepository<User>();
        var loans = new AsyncFakeRepository<Loan>();
        var (book, user) = await SeedPairAsync(books, users);

        await SeedLoanAsync(loans, book, user, LoanStatus.Returned, daysAgo: 30);
        await SeedLoanAsync(loans, book, user, LoanStatus.Cancelled, daysAgo: 20);
        await SeedLoanAsync(loans, book, user, LoanStatus.Active, daysAgo: 1);

        var handler = new GetBookHistoryQueryHandler(new GetBookHistoryQueryValidator(), books, loans);

        // When
        var output = await handler.HandleAsync(new GetBookHistoryQuery { BookId = book.PublicId });

        // Then - a history endpoint that hid the terminal states would perform, one query at a time,
        // exactly the deletion this API refuses to perform
        Assert.True(output.Success);
        Assert.Equal(3, output.Data!.Count);
        Assert.Contains(output.Data, loan => loan.Status == LoanStatus.Returned);
        Assert.Contains(output.Data, loan => loan.Status == LoanStatus.Cancelled);
        Assert.Contains(output.Data, loan => loan.Status == LoanStatus.Active);
    }

    [UnitFact]
    public async Task GivenAHistory_WhenReadingIt_ThenItIsOrderedNewestFirst()
    {
        // Given
        var books = new AsyncFakeRepository<Book>();
        var users = new AsyncFakeRepository<User>();
        var loans = new AsyncFakeRepository<Loan>();
        var (book, user) = await SeedPairAsync(books, users);

        await SeedLoanAsync(loans, book, user, LoanStatus.Returned, daysAgo: 30);
        await SeedLoanAsync(loans, book, user, LoanStatus.Returned, daysAgo: 5);

        var handler = new GetBookHistoryQueryHandler(new GetBookHistoryQueryValidator(), books, loans);

        // When
        var output = await handler.HandleAsync(new GetBookHistoryQuery { BookId = book.PublicId });

        // Then
        Assert.True(output.Data![0].LoanedAt > output.Data[1].LoanedAt);
    }

    [UnitFact]
    public async Task GivenAStatusFilter_WhenReadingBookHistory_ThenOnlyThatStateIsReturned()
    {
        // Given
        var books = new AsyncFakeRepository<Book>();
        var users = new AsyncFakeRepository<User>();
        var loans = new AsyncFakeRepository<Loan>();
        var (book, user) = await SeedPairAsync(books, users);

        await SeedLoanAsync(loans, book, user, LoanStatus.Returned, daysAgo: 30);
        await SeedLoanAsync(loans, book, user, LoanStatus.Active, daysAgo: 1);

        var handler = new GetBookHistoryQueryHandler(new GetBookHistoryQueryValidator(), books, loans);

        // When
        var output = await handler.HandleAsync(
            new GetBookHistoryQuery { BookId = book.PublicId, Status = LoanStatus.Active });

        // Then
        var loan = Assert.Single(output.Data!);
        Assert.Equal(LoanStatus.Active, loan.Status);
    }

    [UnitFact]
    public async Task GivenUnknownBook_WhenReadingHistory_ThenNotFoundIsReportedRatherThanAnEmptyPage()
    {
        // Given
        var books = new AsyncFakeRepository<Book>();
        var loans = new AsyncFakeRepository<Loan>();
        var handler = new GetBookHistoryQueryHandler(new GetBookHistoryQueryValidator(), books, loans);

        // When
        var output = await handler.HandleAsync(new GetBookHistoryQuery { BookId = Guid.NewGuid() });

        // Then - an empty page would be indistinguishable from a book that has never been lent
        Assert.False(output.Success);
        Assert.Contains(BookMessages.BookNotFound, output.Errors);
    }

    [UnitFact]
    public async Task GivenLoansInEveryState_WhenReadingUserLoans_ThenAllOfThemAreReturned()
    {
        // Given
        var books = new AsyncFakeRepository<Book>();
        var users = new AsyncFakeRepository<User>();
        var loans = new AsyncFakeRepository<Loan>();
        var (book, user) = await SeedPairAsync(books, users);

        await SeedLoanAsync(loans, book, user, LoanStatus.Returned, daysAgo: 30);
        await SeedLoanAsync(loans, book, user, LoanStatus.Active, daysAgo: 1);

        var handler = new GetUserLoansQueryHandler(new GetUserLoansQueryValidator(), users, loans);

        // When
        var output = await handler.HandleAsync(new GetUserLoansQuery { UserId = user.PublicId });

        // Then - the reader's history, not their current shelf
        Assert.Equal(2, output.Data!.Count);
        Assert.Contains(UserMessages.UserLoansRetrievedSuccessfully, output.Messages);
    }

    [UnitFact]
    public async Task GivenUnknownUser_WhenReadingTheirLoans_ThenNotFoundIsReported()
    {
        // Given
        var users = new AsyncFakeRepository<User>();
        var loans = new AsyncFakeRepository<Loan>();
        var handler = new GetUserLoansQueryHandler(new GetUserLoansQueryValidator(), users, loans);

        // When
        var output = await handler.HandleAsync(new GetUserLoansQuery { UserId = Guid.NewGuid() });

        // Then
        Assert.False(output.Success);
        Assert.Contains(UserMessages.UserNotFound, output.Errors);
    }
}
