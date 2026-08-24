using Library.Command.Handlers;
using Library.Command.Input.Validation;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Testing.Mock;

namespace Library.Command.Tests.Support;

/// <summary>Assembles the catalogue and reader handlers over in-memory fakes, with a fixed clock.</summary>
public class CatalogHandlerHarness
{
    /// <summary>The instant every handler in this harness sees as "now".</summary>
    public static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public AsyncFakeRepository<Book> Books { get; } = new();
    public AsyncFakeRepository<User> Users { get; } = new();
    public AsyncFakeRepository<Loan> Loans { get; } = new();
    public FakeBookCopyReserver Reserver { get; }
    public FakeAuditEventWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeCacheStore Cache { get; } = new();
    public FakeTimeProvider Clock { get; } = new(Now);

    public CatalogHandlerHarness() => Reserver = new FakeBookCopyReserver(Books);

    public CreateBookCommandHandler CreateBookHandler() =>
        new(new CreateBookCommandValidator(), Books, Audit, UnitOfWork, Cache, Clock);

    public UpdateBookCommandHandler UpdateBookHandler() =>
        new(new UpdateBookCommandValidator(), Books, Reserver, Audit, UnitOfWork, Cache, Clock);

    public DeactivateBookCommandHandler DeactivateBookHandler() =>
        new(Books, Loans, Audit, UnitOfWork, Cache, Clock);

    public CreateUserCommandHandler CreateUserHandler() =>
        new(new CreateUserCommandValidator(), Users, Audit, UnitOfWork, Clock);

    /// <summary>Adds a book to the fake catalogue.</summary>
    public async Task<Book> SeedBookAsync(
        int totalCopies = 3, int? availableCopies = null, bool isActive = true, string? isbn = null)
    {
        var book = new Book
        {
            PublicId = Guid.NewGuid(),
            Title = "The Pragmatic Programmer",
            Isbn = isbn ?? "9780306406157",
            Author = "Hunt and Thomas",
            TotalCopies = totalCopies,
            AvailableCopies = availableCopies ?? totalCopies,
            IsActive = isActive,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        };

        await Books.CreateAsync(book);

        return book;
    }

    /// <summary>Adds a loan of a book, so deactivation has history to preserve or copies to block on.</summary>
    public async Task<Loan> SeedLoanAsync(Book book, LoanStatus status = LoanStatus.Active)
    {
        var user = new User { PublicId = Guid.NewGuid(), Name = "Reader", Email = "reader@test.local" };

        await Users.CreateAsync(user);

        var loan = new Loan
        {
            PublicId = Guid.NewGuid(),
            BookId = book.Id,
            UserId = user.Id,
            Book = book,
            User = user,
            Status = status,
            LoanedAt = Now.UtcDateTime.AddDays(-5),
            DueAt = Now.UtcDateTime.AddDays(9),
            ReturnedAt = status == LoanStatus.Returned ? Now.UtcDateTime.AddDays(-1) : null,
            CancelledAt = status == LoanStatus.Cancelled ? Now.UtcDateTime.AddDays(-1) : null
        };

        await Loans.CreateAsync(loan);

        return loan;
    }
}
