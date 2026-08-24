using System.Net;
using Library.Command.Output;
using Library.Domain.Enums;
using Library.Output;
using Library.Query.Output;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Library.WebApi.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Library.WebApi.Tests;

// The history requirement, end to end: returning or cancelling a loan must preserve the fact that it
// existed, and no endpoint may erase a book's history silently.
[Collection(nameof(FunctionalCollection))]
public class LoanHistoryPreservationTests(LibraryContainers containers) : LibraryApiTest(containers)
{
    [FunctionalFact]
    public async Task GivenAReturnedLoan_WhenReadingTheHistory_ThenTheLoanIsStillThere()
    {
        // Given a lent copy
        var book = await CreateBookAsync(totalCopies: 2);
        var reader = await CreateUserAsync();
        var loan = await CreateLoanAsync(book.Id, reader.Id);
        var loanId = loan.Body!.Data!.Id;

        // When it comes back
        var returned = await Gateway.PostAsync<DataOutput<LoanCommandOutput>>($"/loans/{loanId}/return");

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        Assert.Equal(LoanStatus.Returned, returned.Body!.Data!.Status);

        // Then - the loan is in the book's history, in a terminal state, with its dates intact
        var history = await Gateway.GetAsync<PaginatedOutput<LoanOutput>>($"/books/{book.Id}/history");

        var entry = Assert.Single(history.Body!.Data!);

        Assert.Equal(loanId, entry.Id);
        Assert.Equal(LoanStatus.Returned, entry.Status);
        Assert.NotNull(entry.ReturnedAt);
        Assert.Null(entry.CancelledAt);

        // Compared to the microsecond rather than exactly: PostgreSQL's timestamptz stores
        // microseconds and .NET's DateTime counts 100-nanosecond ticks, so a value that has been
        // through the database is truncated relative to the one the create response carried. What the
        // test is about is that the original loan date survived the return, and it did.
        Assert.True(
            (entry.LoanedAt - loan.Body.Data.LoanedAt).Duration() < TimeSpan.FromMilliseconds(1),
            $"Loan date moved: {loan.Body.Data.LoanedAt:o} became {entry.LoanedAt:o}");

        // Then - and the copy is back on the shelf
        Assert.Equal(2, await ReadAvailableCopiesAsync(book.Id));
    }

    [FunctionalFact]
    public async Task GivenACancelledLoan_WhenReadingTheHistory_ThenItIsKeptAndDistinguishableFromAReturn()
    {
        // Given
        var book = await CreateBookAsync(totalCopies: 2);
        var reader = await CreateUserAsync();
        var loan = await CreateLoanAsync(book.Id, reader.Id);
        var loanId = loan.Body!.Data!.Id;

        // When the loan is called off
        var cancelled = await Gateway.PostAsync<DataOutput<LoanCommandOutput>>(
            $"/loans/{loanId}/cancel", new { reason = "Reader changed their mind" });

        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        // Then - the record stands, and it says cancelled rather than returned. A reader who never
        // took the copy must not appear in the history as one who brought it back.
        var history = await Gateway.GetAsync<PaginatedOutput<LoanOutput>>($"/books/{book.Id}/history");

        var entry = Assert.Single(history.Body!.Data!);

        Assert.Equal(LoanStatus.Cancelled, entry.Status);
        Assert.NotNull(entry.CancelledAt);
        Assert.Null(entry.ReturnedAt);
    }

    [FunctionalFact]
    public async Task GivenAReaderWithFinishedLoans_WhenReadingTheirLoans_ThenTheHistoryIsComplete()
    {
        // Given a reader with one returned loan, one cancelled and one still out
        var reader = await CreateUserAsync();

        var returnedBook = await CreateBookAsync(totalCopies: 1);
        var cancelledBook = await CreateBookAsync(totalCopies: 1);
        var activeBook = await CreateBookAsync(totalCopies: 1);

        var returnedLoan = await CreateLoanAsync(returnedBook.Id, reader.Id);
        var cancelledLoan = await CreateLoanAsync(cancelledBook.Id, reader.Id);
        await CreateLoanAsync(activeBook.Id, reader.Id);

        await Gateway.PostAsync<DataOutput<LoanCommandOutput>>(
            $"/loans/{returnedLoan.Body!.Data!.Id}/return");
        await Gateway.PostAsync<DataOutput<LoanCommandOutput>>(
            $"/loans/{cancelledLoan.Body!.Data!.Id}/cancel");

        // When
        var loans = await Gateway.GetAsync<PaginatedOutput<LoanOutput>>($"/users/{reader.Id}/loans");

        // Then - all three, because this is the reader's history and not their current shelf
        Assert.Equal(3, loans.Body!.Data!.Count);
        Assert.Contains(loans.Body.Data, loan => loan.Status == LoanStatus.Returned);
        Assert.Contains(loans.Body.Data, loan => loan.Status == LoanStatus.Cancelled);
        Assert.Contains(loans.Body.Data, loan => loan.Status == LoanStatus.Active);
    }

    [FunctionalFact]
    public async Task GivenABookWithHistory_WhenDeletingIt_ThenItIsDeactivatedAndTheHistorySurvives()
    {
        // Given a book that has been lent and returned
        var book = await CreateBookAsync(totalCopies: 1);
        var reader = await CreateUserAsync();
        var loan = await CreateLoanAsync(book.Id, reader.Id);

        await Gateway.PostAsync<DataOutput<LoanCommandOutput>>(
            $"/loans/{loan.Body!.Data!.Id}/return");

        // When the book is "deleted"
        var response = await Gateway.DeleteAsync<DataOutput<DeactivateBookCommandOutput>>($"/books/{book.Id}");

        // Then - it was deactivated, not removed, and the response says what it kept
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Body!.Data!.IsActive);
        Assert.Equal(1, response.Body.Data.PreservedLoanCount);

        // Then - the row and its history are both still in the database
        await using var context = Containers.CreateContext();

        Assert.True(await context.Books.AnyAsync(entity => entity.PublicId == book.Id));
        Assert.Equal(1, await context.Loans.CountAsync(entity => entity.Book.PublicId == book.Id));

        // Then - and the history endpoint still answers for it
        var history = await Gateway.GetAsync<PaginatedOutput<LoanOutput>>($"/books/{book.Id}/history");

        Assert.Single(history.Body!.Data!);
    }

    [FunctionalFact]
    public async Task GivenABookWithCopiesOut_WhenDeletingIt_ThenItIsRefused()
    {
        // Given a book with a copy still with a reader
        var book = await CreateBookAsync(totalCopies: 1);
        var reader = await CreateUserAsync();

        await CreateLoanAsync(book.Id, reader.Id);

        // When
        var response = await Gateway.DeleteAsync<DataOutput<DeactivateBookCommandOutput>>($"/books/{book.Id}");

        // Then
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("while copies are on loan", response.RawBody);
    }

    [FunctionalFact]
    public async Task GivenAnInactiveBook_WhenLending_ThenItIsRefused()
    {
        // Given a withdrawn title
        var book = await CreateBookAsync(totalCopies: 1);

        await Gateway.DeleteAsync<DataOutput<DeactivateBookCommandOutput>>($"/books/{book.Id}");

        var reader = await CreateUserAsync();

        // When
        var response = await CreateLoanAsync(book.Id, reader.Id);

        // Then - deactivation is what stops new loans; the existing history is untouched by it
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("not available for lending", response.RawBody);
    }

    [FunctionalFact]
    public async Task GivenAReturnedLoan_WhenReturningItAgain_ThenItIsRefusedAndTheShelfIsNotCreditedTwice()
    {
        // Given a loan that already came back
        var book = await CreateBookAsync(totalCopies: 2);
        var reader = await CreateUserAsync();
        var loan = await CreateLoanAsync(book.Id, reader.Id);
        var loanId = loan.Body!.Data!.Id;

        await Gateway.PostAsync<DataOutput<LoanCommandOutput>>($"/loans/{loanId}/return");

        // When
        var response = await Gateway.PostAsync<DataOutput<LoanCommandOutput>>($"/loans/{loanId}/return");

        // Then
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, await ReadAvailableCopiesAsync(book.Id));
    }
}
