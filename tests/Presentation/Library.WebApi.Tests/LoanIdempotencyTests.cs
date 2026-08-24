using System.Net;
using Library.Command.Output;
using Library.Output;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Library.WebApi.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Library.WebApi.Tests;

// The idempotency requirement, end to end: repeating a request with the same Idempotency-Key must
// neither create a second loan nor reduce availability twice, and must answer with the response the
// first attempt produced.
[Collection(nameof(FunctionalCollection))]
public class LoanIdempotencyTests(LibraryContainers containers) : LibraryApiTest(containers)
{
    [FunctionalFact]
    public async Task GivenTheSameKeyAndBody_WhenPostingTwice_ThenOneLoanExistsAndAvailabilityMovesOnce()
    {
        // Given
        var book = await CreateBookAsync(totalCopies: 3);
        var reader = await CreateUserAsync();
        var key = Guid.NewGuid().ToString();

        // When the identical request is sent twice
        var first = await CreateLoanAsync(book.Id, reader.Id, key);
        var second = await CreateLoanAsync(book.Id, reader.Id, key);

        // Then - both succeed, and both describe the same loan
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(first.Body!.Data!.Id, second.Body!.Data!.Id);

        // Then - the second is flagged as a replay rather than pretending to be new work
        Assert.False(first.Body.Data.Replayed);
        Assert.True(second.Body.Data.Replayed);

        // Then - the two assertions the requirement is actually about
        Assert.Equal(2, await ReadAvailableCopiesAsync(book.Id));

        await using var context = Containers.CreateContext();

        Assert.Equal(1, await context.Loans.CountAsync(loan => loan.Book.PublicId == book.Id));
    }

    [FunctionalFact]
    public async Task GivenTheSameKeyRepeatedManyTimes_WhenPosting_ThenAvailabilityStillMovesOnce()
    {
        // Given - a client retrying hard, which is what an idempotency key is for
        var book = await CreateBookAsync(totalCopies: 2);
        var reader = await CreateUserAsync();
        var key = Guid.NewGuid().ToString();

        // When - the first is awaited so the key is committed, then five retries go out together
        var first = await CreateLoanAsync(book.Id, reader.Id, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var retries = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => CreateLoanAsync(book.Id, reader.Id, key, gateway: NewGateway())));

        // Then - every retry replays the same loan
        Assert.All(retries, response =>
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(first.Body!.Data!.Id, response.Body!.Data!.Id);
            Assert.True(response.Body.Data.Replayed);
        });

        // Then - one copy off the shelf, one loan on record
        Assert.Equal(1, await ReadAvailableCopiesAsync(book.Id));

        await using var context = Containers.CreateContext();

        Assert.Equal(1, await context.Loans.CountAsync(loan => loan.Book.PublicId == book.Id));
    }

    [FunctionalFact]
    public async Task GivenSimultaneousRequestsSharingOneKey_WhenPosting_ThenNoSecondLoanIsEverCreated()
    {
        // Given - the hard case: the retries arrive before the first request has committed, so the
        // store's lookup cannot see it yet and the unique index has to do the work
        var book = await CreateBookAsync(totalCopies: 5);
        var reader = await CreateUserAsync();
        var key = Guid.NewGuid().ToString();

        // When six identical requests go out at once
        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => CreateLoanAsync(book.Id, reader.Id, key, gateway: NewGateway())));

        // Then - at least one succeeded, and any that lost the race were told to retry rather than
        // being allowed to create a second loan
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);

        Assert.All(responses, response =>
            Assert.True(
                response.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict,
                $"Unexpected status {response.StatusCode}: {response.RawBody}"));

        // Then - one loan, one copy taken. This is the assertion the whole mechanism exists for.
        await using var context = Containers.CreateContext();

        Assert.Equal(1, await context.Loans.CountAsync(loan => loan.Book.PublicId == book.Id));
        Assert.Equal(4, await ReadAvailableCopiesAsync(book.Id));
    }

    [FunctionalFact]
    public async Task GivenTheSameKeyWithADifferentBody_WhenPosting_ThenItIsRefused()
    {
        // Given a key already used for one book
        var firstBook = await CreateBookAsync(totalCopies: 2);
        var secondBook = await CreateBookAsync(totalCopies: 2);
        var reader = await CreateUserAsync();
        var key = Guid.NewGuid().ToString();

        await CreateLoanAsync(firstBook.Id, reader.Id, key);

        // When the same key is presented for a different book
        var response = await CreateLoanAsync(secondBook.Id, reader.Id, key);

        // Then - refused rather than answered with the first loan, which the caller did not ask for
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already used with a different request", response.RawBody);
        Assert.Equal(2, await ReadAvailableCopiesAsync(secondBook.Id));
    }

    [FunctionalFact]
    public async Task GivenNoIdempotencyKey_WhenPosting_ThenTheRequestIsRefused()
    {
        // Given
        var book = await CreateBookAsync(totalCopies: 1);
        var reader = await CreateUserAsync();

        // When the header is absent
        var response = await Gateway.PostAsync<DataOutput<LoanCommandOutput>>(
            "/loans", new { bookId = book.Id, userId = reader.Id });

        // Then - required rather than optional for this endpoint, so its absence is an input error
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Idempotency-Key", response.RawBody);
        Assert.Equal(1, await ReadAvailableCopiesAsync(book.Id));
    }

    [FunctionalFact]
    public async Task GivenADifferentKeyForTheSameRequest_WhenPosting_ThenItIsATrueSecondAttempt()
    {
        // Given a reader who already holds this title
        var book = await CreateBookAsync(totalCopies: 3);
        var reader = await CreateUserAsync();

        await CreateLoanAsync(book.Id, reader.Id);

        // When the same request arrives under a fresh key - a new intent, not a retry
        var response = await CreateLoanAsync(book.Id, reader.Id);

        // Then - it is evaluated on its merits and refused by the business rule, not silently
        // replayed as though it were the same request
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already has an active loan", response.RawBody);
    }
}
