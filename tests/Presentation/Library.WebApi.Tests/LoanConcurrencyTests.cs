using System.Net;
using Library.Command.Output;
using Library.Domain.Enums;
using Library.Output;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Library.WebApi.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Library.WebApi.Tests;

// The scenario the challenge names explicitly: two simultaneous requests for the last available copy
// must produce exactly one loan, one clear business refusal, and no inconsistent state. Run end to
// end through the HTTP pipeline against a real PostgreSQL, because that is the only arrangement in
// which the claim means anything.
[Collection(nameof(FunctionalCollection))]
public class LoanConcurrencyTests(LibraryContainers containers) : LibraryApiTest(containers)
{
    [FunctionalFact]
    public async Task GivenOneCopyAndTwoSimultaneousRequests_WhenLending_ThenExactlyOneLoanIsCreated()
    {
        // Given a book with a single copy, and two readers who both want it
        var book = await CreateBookAsync(totalCopies: 1);
        var firstReader = await CreateUserAsync();
        var secondReader = await CreateUserAsync();

        // Separate clients, so the two requests are genuinely independent rather than queued behind
        // one another's default headers.
        var firstGateway = NewGateway();
        var secondGateway = NewGateway();

        // When both requests are in flight at once
        var responses = await Task.WhenAll(
            CreateLoanAsync(book.Id, firstReader.Id, gateway: firstGateway),
            CreateLoanAsync(book.Id, secondReader.Id, gateway: secondGateway));

        // Then - exactly one succeeded
        var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        var refused = responses.Count(response => response.StatusCode == HttpStatusCode.Conflict);

        Assert.Equal(1, created);
        Assert.Equal(1, refused);

        // Then - the loser got a clear business refusal, not a 500 and not a validation error
        var refusal = responses.First(response => response.StatusCode == HttpStatusCode.Conflict);

        Assert.Contains("No copies of this book are currently available.", refusal.RawBody);

        // Then - the shelf is at zero, never negative, and exactly one loan exists
        Assert.Equal(0, await ReadAvailableCopiesAsync(book.Id));

        await using var context = Containers.CreateContext();

        var loans = await context.Loans
            .Where(loan => loan.Book.PublicId == book.Id)
            .ToListAsync();

        Assert.Single(loans);
        Assert.Equal(LoanStatus.Active, loans[0].Status);
    }

    [FunctionalFact]
    public async Task GivenOneCopyAndTenSimultaneousRequests_WhenLending_ThenStillExactlyOneLoanIsCreated()
    {
        // Given - the same race widened, so the single winner is not an artefact of there being only
        // two contenders
        var book = await CreateBookAsync(totalCopies: 1);

        var readers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CreateUserAsync()));

        // When
        var responses = await Task.WhenAll(
            readers.Select(reader => CreateLoanAsync(book.Id, reader.Id, gateway: NewGateway())));

        // Then
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(9, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(0, await ReadAvailableCopiesAsync(book.Id));

        await using var context = Containers.CreateContext();

        Assert.Equal(1, await context.Loans.CountAsync(loan => loan.Book.PublicId == book.Id));
    }

    [FunctionalFact]
    public async Task GivenThreeCopiesAndFifteenSimultaneousRequests_WhenLending_ThenExactlyThreeLoansAreCreated()
    {
        // Given - the general case: the number of loans must equal the number of copies, not merely
        // be at most one
        var book = await CreateBookAsync(totalCopies: 3);

        var readers = await Task.WhenAll(Enumerable.Range(0, 15).Select(_ => CreateUserAsync()));

        // When
        var responses = await Task.WhenAll(
            readers.Select(reader => CreateLoanAsync(book.Id, reader.Id, gateway: NewGateway())));

        // Then
        Assert.Equal(3, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(0, await ReadAvailableCopiesAsync(book.Id));

        await using var context = Containers.CreateContext();

        Assert.Equal(3, await context.Loans.CountAsync(loan => loan.Book.PublicId == book.Id));
    }

    [FunctionalFact]
    public async Task GivenConcurrentLendingAndReturning_WhenTheyOverlap_ThenTheCountNeverLeavesItsBounds()
    {
        // Given a book with two copies, both lent
        var book = await CreateBookAsync(totalCopies: 2);
        var firstReader = await CreateUserAsync();
        var secondReader = await CreateUserAsync();

        var firstLoan = await CreateLoanAsync(book.Id, firstReader.Id);
        var secondLoan = await CreateLoanAsync(book.Id, secondReader.Id);

        Assert.Equal(HttpStatusCode.Created, firstLoan.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondLoan.StatusCode);

        var thirdReader = await CreateUserAsync();

        // When two returns and a new loan attempt all happen at once
        await Task.WhenAll(
            NewGateway().PostAsync<DataOutput<LoanCommandOutput>>(
                $"/loans/{firstLoan.Body!.Data!.Id}/return"),
            NewGateway().PostAsync<DataOutput<LoanCommandOutput>>(
                $"/loans/{secondLoan.Body!.Data!.Id}/return"),
            CreateLoanAsync(book.Id, thirdReader.Id, gateway: NewGateway()));

        // Then - whatever interleaving happened, the invariant holds: the count is within its bounds
        // and agrees exactly with the number of loans still out. The check constraints would have
        // refused a write that broke the first half; this asserts the second.
        await using var context = Containers.CreateContext();

        var stored = await context.Books.FirstAsync(entity => entity.PublicId == book.Id);

        var activeLoans = await context.Loans.CountAsync(
            loan => loan.Book.PublicId == book.Id && loan.Status == LoanStatus.Active);

        Assert.InRange(stored.AvailableCopies, 0, stored.TotalCopies);
        Assert.Equal(stored.TotalCopies - activeLoans, stored.AvailableCopies);
    }
}
