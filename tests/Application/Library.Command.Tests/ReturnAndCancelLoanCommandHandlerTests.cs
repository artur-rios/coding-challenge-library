using Library.Command.Input;
using Library.Command.Tests.Support;
using Library.Domain.Enums;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Library.Testing.Attributes;

namespace Library.Command.Tests;

// Unit tests for ReturnLoanCommandHandler and CancelLoanCommandHandler. The two are tested together
// because the interesting property is shared and comparative: both release the copy, both keep the
// row, and the states they leave behind must stay distinguishable.
public class ReturnAndCancelLoanCommandHandlerTests
{
    [UnitFact]
    public async Task GivenActiveLoan_WhenReturning_ThenCopyGoesBackAndLoanIsKept()
    {
        // Given a book with one copy out
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2, availableCopies: 1);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user);

        // When
        var output = await harness.ReturnLoanHandler()
            .HandleAsync(new ReturnLoanCommand { Id = loan.PublicId });

        // Then - the copy is back and the response says so
        Assert.True(output.Success);
        Assert.Equal(LoanStatus.Returned, output.Data!.Status);
        Assert.Equal(2, output.Data.AvailableCopies);
        Assert.Equal(2, book.AvailableCopies);

        // Then - the loan still exists, now terminal, with its original dates intact
        var stored = Assert.Single(harness.Loans.Items);
        Assert.Equal(LoanStatus.Returned, stored.Status);
        Assert.Equal(LoanHandlerHarness.Now.UtcDateTime, stored.ReturnedAt);
        Assert.Null(stored.CancelledAt);
        Assert.Equal(loan.LoanedAt, stored.LoanedAt);
    }

    [UnitFact]
    public async Task GivenActiveLoan_WhenCancelling_ThenCopyGoesBackAndLoanIsKeptAsCancelled()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2, availableCopies: 1);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user);

        // When
        var output = await harness.CancelLoanHandler()
            .HandleAsync(new CancelLoanCommand { Id = loan.PublicId, Reason = "Reader changed their mind" });

        // Then
        Assert.True(output.Success);
        Assert.Equal(LoanStatus.Cancelled, output.Data!.Status);
        Assert.Equal(2, book.AvailableCopies);

        // Then - cancelled is not returned, and the row records which of the two happened
        var stored = Assert.Single(harness.Loans.Items);
        Assert.Equal(LoanStatus.Cancelled, stored.Status);
        Assert.Equal(LoanHandlerHarness.Now.UtcDateTime, stored.CancelledAt);
        Assert.Null(stored.ReturnedAt);
    }

    [UnitFact]
    public async Task GivenReturnedLoan_WhenReturningAgain_ThenSecondCallIsRefused()
    {
        // Given a loan that already came back
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user, LoanStatus.Returned);

        // When
        var output = await harness.ReturnLoanHandler()
            .HandleAsync(new ReturnLoanCommand { Id = loan.PublicId });

        // Then - refused, so the copy is not credited to the shelf twice
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.LoanAlreadyReturned, output.Errors);
        Assert.Equal(0, harness.Reserver.Releases);
    }

    [UnitFact]
    public async Task GivenCancelledLoan_WhenReturning_ThenTheCancellationIsReported()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user, LoanStatus.Cancelled);

        // When
        var output = await harness.ReturnLoanHandler()
            .HandleAsync(new ReturnLoanCommand { Id = loan.PublicId });

        // Then - the message names the state the loan is actually in, not a generic conflict
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.LoanAlreadyCancelled, output.Errors);
    }

    [UnitFact]
    public async Task GivenConcurrentTransition_WhenReturning_ThenTheLoserIsRefusedAndNoCopyIsCredited()
    {
        // Given a loan another request moves first, so the conditional update matches no row
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2, availableCopies: 1);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user);
        harness.Transitions.NextTransitionFails = true;

        // When
        var output = await harness.ReturnLoanHandler()
            .HandleAsync(new ReturnLoanCommand { Id = loan.PublicId });

        // Then - this is the guard that stops one loan crediting two copies
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.LoanAlreadyReturned, output.Errors);
        Assert.Equal(1, book.AvailableCopies);
        Assert.False(harness.UnitOfWork.Committed);
    }

    [UnitFact]
    public async Task GivenReleaseThatWouldExceedTheTotal_WhenReturning_ThenInconsistencyIsReported()
    {
        // Given a release that the conditional update refuses - which can only mean the counts and the
        // loans have diverged
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2, availableCopies: 1);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user);
        harness.Reserver.NextReleaseFails = true;

        // When
        var output = await harness.ReturnLoanHandler()
            .HandleAsync(new ReturnLoanCommand { Id = loan.PublicId });

        // Then - reported rather than ignored, and the transition is rolled back with it
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.AvailabilityInconsistent, output.Errors);
        Assert.False(harness.UnitOfWork.Committed);
    }

    [UnitFact]
    public async Task GivenUnknownLoan_WhenReturning_ThenNotFoundIsReported()
    {
        // Given
        var harness = new LoanHandlerHarness();

        // When
        var output = await harness.ReturnLoanHandler()
            .HandleAsync(new ReturnLoanCommand { Id = Guid.NewGuid() });

        // Then
        Assert.False(output.Success);
        Assert.Contains(LoanMessages.LoanNotFound, output.Errors);
    }

    [UnitFact]
    public async Task GivenOverdueLoan_WhenReturning_ThenAuditEntryRecordsThatItWasLate()
    {
        // Given a loan returned after its due date
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1, availableCopies: 0);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user);
        harness.Clock.Advance(TimeSpan.FromDays(30));

        // When
        await harness.ReturnLoanHandler().HandleAsync(new ReturnLoanCommand { Id = loan.PublicId });

        // Then - the trail carries the fact rather than leaving it to be recomputed from timestamps
        var entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditActions.LoanReturned, entry.Action);
        Assert.Equal(true, entry.Data.GetType().GetProperty("WasOverdue")!.GetValue(entry.Data));
    }

    [UnitFact]
    public async Task GivenCancelledLoan_WhenCancelling_ThenCachesAreDroppedOnlyOnce()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2, availableCopies: 1);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user);
        var handler = harness.CancelLoanHandler();

        // When - cancelled, then cancelled again
        await handler.HandleAsync(new CancelLoanCommand { Id = loan.PublicId });
        harness.Cache.RemovedKeys.Clear();
        var second = await handler.HandleAsync(new CancelLoanCommand { Id = loan.PublicId });

        // Then - the repeat changes nothing, so it invalidates nothing
        Assert.False(second.Success);
        Assert.Contains(LoanMessages.LoanAlreadyCancelled, second.Errors);
        Assert.Empty(harness.Cache.RemovedKeys);
    }

    [UnitFact]
    public async Task GivenCancellation_WhenCancelling_ThenReasonIsRecordedOnTheAuditEntry()
    {
        // Given
        var harness = new LoanHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 1, availableCopies: 0);
        var user = await harness.SeedUserAsync();
        var loan = await harness.SeedLoanAsync(book, user);

        // When
        await harness.CancelLoanHandler()
            .HandleAsync(new CancelLoanCommand { Id = loan.PublicId, Reason = "Lost in transit" });

        // Then
        var entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditActions.LoanCancelled, entry.Action);
        Assert.Equal("Lost in transit", entry.Data.GetType().GetProperty("Reason")!.GetValue(entry.Data));
    }
}
