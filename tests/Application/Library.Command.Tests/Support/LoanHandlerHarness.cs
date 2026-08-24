using FluentValidation;
using Library.Command.Handlers;
using Library.Command.Input;
using Library.Command.Input.Validation;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Shared.Telemetry;
using Library.Testing.Mock;
using Microsoft.Extensions.Logging.Abstractions;

namespace Library.Command.Tests.Support;

/// <summary>
///     Assembles the loan handlers over in-memory fakes, with a fixed clock, and seeds the books and
///     readers a test needs.
/// </summary>
/// <remarks>
///     The three loan handlers share nine dependencies. Building them inline in every test would bury
///     the one line that differs - which is the line the test is about - under eight that never do.
/// </remarks>
public class LoanHandlerHarness
{
    /// <summary>The instant every handler in this harness sees as "now".</summary>
    public static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    public AsyncFakeRepository<Book> Books { get; } = new();
    public AsyncFakeRepository<User> Users { get; } = new();
    public AsyncFakeRepository<Loan> Loans { get; } = new();
    public FakeBookCopyReserver Reserver { get; }
    public FakeLoanTransitionWriter Transitions { get; }
    public FakeIdempotencyStore Idempotency { get; } = new();
    public FakeAuditEventWriter Audit { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakeCacheStore Cache { get; } = new();
    public LibraryMetrics Metrics { get; } = TestMetrics.Create();
    public FakeTimeProvider Clock { get; } = new(Now);

    public LoanHandlerHarness()
    {
        Reserver = new FakeBookCopyReserver(Books);
        Transitions = new FakeLoanTransitionWriter(Loans);
    }

    /// <summary>Adds a book to the fake catalogue.</summary>
    public async Task<Book> SeedBookAsync(int totalCopies = 1, int? availableCopies = null, bool isActive = true)
    {
        var book = new Book
        {
            PublicId = Guid.NewGuid(),
            Title = $"Title {Guid.NewGuid():N}",
            Isbn = "9780306406157",
            Author = "Author",
            TotalCopies = totalCopies,
            AvailableCopies = availableCopies ?? totalCopies,
            IsActive = isActive,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime
        };

        await Books.CreateAsync(book);

        return book;
    }

    /// <summary>Adds a reader to the fake register.</summary>
    public async Task<User> SeedUserAsync()
    {
        var user = new User
        {
            PublicId = Guid.NewGuid(),
            Name = "Reader",
            Email = $"reader-{Guid.NewGuid():N}@test.local",
            CreatedAt = Now.UtcDateTime
        };

        await Users.CreateAsync(user);

        return user;
    }

    /// <summary>Adds a loan in the given state, without going through the handler.</summary>
    public async Task<Loan> SeedLoanAsync(Book book, User user, LoanStatus status = LoanStatus.Active)
    {
        var loan = new Loan
        {
            PublicId = Guid.NewGuid(),
            BookId = book.Id,
            UserId = user.Id,
            Book = book,
            User = user,
            Status = status,
            LoanedAt = Now.UtcDateTime.AddDays(-1),
            DueAt = Now.UtcDateTime.AddDays(13),
            ReturnedAt = status == LoanStatus.Returned ? Now.UtcDateTime : null,
            CancelledAt = status == LoanStatus.Cancelled ? Now.UtcDateTime : null
        };

        await Loans.CreateAsync(loan);

        return loan;
    }

    /// <summary>The loan-creation handler over this harness.</summary>
    public CreateLoanCommandHandler CreateLoanHandler(IValidator<CreateLoanCommand>? validator = null) =>
        new(validator ?? new CreateLoanCommandValidator(),
            Books, Users, Loans, Reserver, Idempotency, Audit, UnitOfWork, Cache, Metrics, Clock,
            NullLogger<CreateLoanCommandHandler>.Instance);

    /// <summary>The return handler over this harness.</summary>
    public ReturnLoanCommandHandler ReturnLoanHandler() =>
        new(Loans, Books, Transitions, Reserver, Audit, UnitOfWork, Cache, Clock,
            NullLogger<ReturnLoanCommandHandler>.Instance);

    /// <summary>The cancellation handler over this harness.</summary>
    public CancelLoanCommandHandler CancelLoanHandler() =>
        new(Loans, Books, Transitions, Reserver, Audit, UnitOfWork, Cache, Clock,
            NullLogger<CancelLoanCommandHandler>.Instance);

    /// <summary>A well-formed loan command for the given book and reader.</summary>
    public static CreateLoanCommand Command(Book book, User user, string? idempotencyKey = null, int loanDays = 14) =>
        new()
        {
            BookId = book.PublicId,
            UserId = user.PublicId,
            LoanDays = loanDays,
            IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString()
        };
}
