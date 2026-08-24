using Library.Command.Handlers;
using Library.Command.Tests.Support;
using Library.Domain.Enums;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Library.Testing.Attributes;

namespace Library.Command.Tests;

// Unit tests for CreateLoanCommandHandler: the main flow, the refusals it can produce, the
// idempotent replay, the concurrent-key collision, and what it leaves in the audit trail and the
// cache. The atomicity of the reservation itself is not tested here - no in-memory fake can
// demonstrate it - and is covered against a real PostgreSQL in Library.Data.Tests and
// Library.WebApi.Tests.
public class CreateLoanCommandHandlerTests
{
    [UnitFact]
    public async Task GivenAvailableCopy_WhenCreatingLoan_ThenLoanIsCreatedAndCopyIsTaken()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2);
        var user = await harness.SeedUserAsync();

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then - the response
        Assert.True(output.Success);
        Assert.Equal(book.PublicId, output.Data!.BookId);
        Assert.Equal(user.PublicId, output.Data.UserId);
        Assert.Equal(LoanStatus.Active, output.Data.Status);
        Assert.False(output.Data.Replayed);
        Assert.Contains(LoanMessages.LoanCreatedSuccessfully, output.Messages);

        // Then - one copy came off the shelf, and the loan is on record
        Assert.Equal(1, output.Data.AvailableCopies);
        Assert.Equal(1, book.AvailableCopies);
        Assert.Single(harness.Loans.Items);
        Assert.True(harness.UnitOfWork.Committed);
    }

    [UnitFact]
    public async Task GivenDefaultLoanPeriod_WhenCreatingLoan_ThenDueDateIsFourteenDaysOut()
    {
        // Given a clock that stands still, so the due date can be asserted exactly
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync();
        var user = await harness.SeedUserAsync();

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then
        Assert.Equal(LoanHandlerHarness.Now.UtcDateTime, output.Data!.LoanedAt);
        Assert.Equal(LoanHandlerHarness.Now.UtcDateTime.AddDays(14), output.Data.DueAt);
    }

    [UnitFact]
    public async Task GivenNoCopiesAvailable_WhenCreatingLoan_ThenLoanIsRefusedAndNothingIsWritten()
    {
        // Given a book whose only copy is already out
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1, availableCopies: 0);
        var user = await harness.SeedUserAsync();

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then - refused with the business message, not an exception
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.NoCopiesAvailable, output.Errors);

        // Then - no loan, no negative count, and the transaction did not commit
        Assert.Empty(harness.Loans.Items);
        Assert.Equal(0, book.AvailableCopies);
        Assert.False(harness.UnitOfWork.Committed);
    }

    [UnitFact]
    public async Task GivenInactiveBook_WhenCreatingLoan_ThenLoanIsRefusedWithTheWithdrawnMessage()
    {
        // Given a book withdrawn from lending, which still has copies on the shelf
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 3, isActive: false);
        var user = await harness.SeedUserAsync();

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then - the caller is told why, rather than being told nothing is available
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.BookNotAvailableForLoan, output.Errors);
        Assert.Equal(3, book.AvailableCopies);
    }

    [UnitFact]
    public async Task GivenUnknownBook_WhenCreatingLoan_ThenNotFoundIsReported()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var user = await harness.SeedUserAsync();
        var missingBook = await harness.SeedBookAsync();
        missingBook.PublicId = Guid.NewGuid();

        // When - the command names a book identifier nothing holds
        var output = await harness.CreateLoanHandler().HandleAsync(new Input.CreateLoanCommand
        {
            BookId = Guid.NewGuid(),
            UserId = user.PublicId,
            IdempotencyKey = Guid.NewGuid().ToString()
        });

        // Then
        Assert.False(output.Success);
        Assert.Contains(BookMessages.BookNotFound, output.Errors);
    }

    [UnitFact]
    public async Task GivenUnknownUser_WhenCreatingLoan_ThenNotFoundIsReported()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync();

        // When
        var output = await harness.CreateLoanHandler().HandleAsync(new Input.CreateLoanCommand
        {
            BookId = book.PublicId,
            UserId = Guid.NewGuid(),
            IdempotencyKey = Guid.NewGuid().ToString()
        });

        // Then
        Assert.False(output.Success);
        Assert.Contains(UserMessages.UserNotFound, output.Errors);
    }

    [UnitFact]
    public async Task GivenReaderAlreadyHoldsTheTitle_WhenCreatingLoan_ThenSecondLoanIsRefused()
    {
        // Given a reader with an active loan of this book
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 5);
        var user = await harness.SeedUserAsync();
        await harness.SeedLoanAsync(book, user);

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.UserAlreadyHasCopy, output.Errors);
    }

    [UnitFact]
    public async Task GivenMissingIdempotencyKey_WhenCreatingLoan_ThenRequestIsRefused()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync();
        var user = await harness.SeedUserAsync();

        // When - the header was absent, so the controller applied an empty key
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user, idempotencyKey: string.Empty));

        // Then - refused before anything is read or written
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.IdempotencyKeyRequired, output.Errors);
        Assert.Empty(harness.Loans.Items);
    }

    [UnitFact]
    public async Task GivenSameKeyAndSameRequest_WhenCreatingLoanTwice_ThenSecondIsReplayedWithoutANewLoan()
    {
        // Given a loan already made with a known key
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 5);
        var user = await harness.SeedUserAsync();
        var key = Guid.NewGuid().ToString();
        var handler = harness.CreateLoanHandler();

        var first = await handler.HandleAsync(LoanHandlerHarness.Command(book, user, key));

        // When - the identical request arrives again
        var second = await handler.HandleAsync(LoanHandlerHarness.Command(book, user, key));

        // Then - the same loan comes back, flagged as a replay
        Assert.True(second.Success);
        Assert.Equal(first.Data!.Id, second.Data!.Id);
        Assert.True(second.Data.Replayed);
        Assert.False(first.Data.Replayed);

        // Then - and, crucially, availability moved once rather than twice
        Assert.Single(harness.Loans.Items);
        Assert.Equal(4, book.AvailableCopies);
        Assert.Equal(1, harness.Reserver.Reservations);
    }

    [UnitFact]
    public async Task GivenSameKeyAndDifferentRequest_WhenCreatingLoan_ThenReuseIsRefused()
    {
        // Given a key already used for one book
        var harness = new LoanHandlerHarness();
        var firstBook = await harness.SeedBookAsync(totalCopies: 5);
        var secondBook = await harness.SeedBookAsync(totalCopies: 5);
        var user = await harness.SeedUserAsync();
        var key = Guid.NewGuid().ToString();
        var handler = harness.CreateLoanHandler();

        await handler.HandleAsync(LoanHandlerHarness.Command(firstBook, user, key));

        // When - the same key is presented for a different book
        var output = await handler.HandleAsync(LoanHandlerHarness.Command(secondBook, user, key));

        // Then - refused rather than answered with the first loan, which the caller did not ask for
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.IdempotencyKeyReused, output.Errors);
        Assert.Equal(5, secondBook.AvailableCopies);
    }

    [UnitFact]
    public async Task GivenConcurrentRequestClaimedTheKey_WhenCreatingLoan_ThenAttemptIsRolledBack()
    {
        // Given a store that will report the key as taken when this attempt tries to record it -
        // which is what happens when a request carrying the same key commits first
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1);
        var user = await harness.SeedUserAsync();
        harness.Idempotency.NextSaveCollides = true;

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then - the caller is told to retry, and no second loan stands
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.IdempotentRequestInFlight, output.Errors);
        Assert.False(harness.UnitOfWork.Committed);
    }

    [UnitFact]
    public async Task GivenCreatedLoan_WhenCreatingLoan_ThenAuditEntryRecordsTheBusinessFacts()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2);
        var user = await harness.SeedUserAsync();
        var key = Guid.NewGuid().ToString();

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user, key));

        // Then - one entry, naming the loan, the action, and enough to understand the change
        var entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditEntityTypes.Loan, entry.EntityType);
        Assert.Equal(AuditActions.LoanCreated, entry.Action);
        Assert.Equal(output.Data!.Id, entry.EntityId);

        var data = entry.Data.GetType();
        Assert.Equal(book.PublicId, data.GetProperty("BookId")!.GetValue(entry.Data));
        Assert.Equal(user.PublicId, data.GetProperty("UserId")!.GetValue(entry.Data));
        Assert.Equal(key, data.GetProperty("IdempotencyKey")!.GetValue(entry.Data));
    }

    [UnitFact]
    public async Task GivenRefusedLoan_WhenCreatingLoan_ThenNoAuditEntryIsWritten()
    {
        // Given a book with nothing on the shelf
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1, availableCopies: 0);
        var user = await harness.SeedUserAsync();

        // When
        await harness.CreateLoanHandler().HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then - the trail records what the library did, and nothing was done. A refusal that changed
        // no state is a log line, not a business event.
        Assert.Empty(harness.Audit.Entries);
    }

    [UnitFact]
    public async Task GivenCreatedLoan_WhenCreatingLoan_ThenAvailabilityAndCatalogCachesAreDropped()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2);
        var user = await harness.SeedUserAsync();

        // When
        await harness.CreateLoanHandler().HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then - the book's own availability entry, and the listing prefix whose keys a writer cannot
        // enumerate
        Assert.Contains(CacheKeys.Availability(book.PublicId), harness.Cache.RemovedKeys);
        Assert.Contains(CacheKeys.CatalogPrefix, harness.Cache.RemovedKeys);
    }

    [UnitFact]
    public async Task GivenRefusedLoan_WhenCreatingLoan_ThenCachesAreLeftAlone()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1, availableCopies: 0);
        var user = await harness.SeedUserAsync();

        // When
        await harness.CreateLoanHandler().HandleAsync(LoanHandlerHarness.Command(book, user));

        // Then - nothing changed, so nothing needed dropping
        Assert.Empty(harness.Cache.RemovedKeys);
    }

    [UnitFact]
    public async Task GivenOutOfRangeLoanPeriod_WhenCreatingLoan_ThenValidationRefusesIt()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync();
        var user = await harness.SeedUserAsync();

        // When
        var output = await harness.CreateLoanHandler()
            .HandleAsync(LoanHandlerHarness.Command(book, user, loanDays: 365));

        // Then
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.LoanPeriodOutOfRange, output.Errors);
    }
}
