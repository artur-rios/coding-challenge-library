using System.Net;
using Library.Command.Output;
using Library.Output;
using Library.Query.Output;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Library.WebApi.Tests.Support;

namespace Library.WebApi.Tests;

// The catalogue endpoints end to end, including the cache behaviour the challenge asks for: a read
// served from Redis, and an invalidation that keeps it coherent when a write moves the numbers.
[Collection(nameof(FunctionalCollection))]
public class CatalogEndpointTests(LibraryContainers containers) : LibraryApiTest(containers)
{
    private ICacheStore Cache => GetService<ICacheStore>();

    [FunctionalFact]
    public async Task GivenAValidRequest_WhenCreatingABook_ThenItIsCreatedWithProblemDetailsOnlyForFailures()
    {
        // Given / When
        var response = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "Clean Architecture", isbn = UniqueIsbn(), author = "Martin", totalCopies = 4
        });

        // Then - a success carries the envelope with its payload and message
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(4, response.Body!.Data!.AvailableCopies);
        Assert.Contains(BookMessages.BookCreatedSuccessfully, response.Body.Messages);
    }

    [FunctionalFact]
    public async Task GivenAnInvalidRequest_WhenCreatingABook_ThenTheFailureIsProblemDetails()
    {
        // When
        var response = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "", isbn = "nonsense", author = "", totalCopies = -1
        });

        // Then - RFC 9457, with every message rather than only the first, and the correlation
        // identifier a caller would quote in a bug report
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"title\"", response.RawBody);
        Assert.Contains("\"errors\"", response.RawBody);
        Assert.Contains("\"correlationId\"", response.RawBody);
        Assert.Contains(BookMessages.IsbnInvalid, response.RawBody);
        Assert.Contains(BookMessages.TitleRequired, response.RawBody);
    }

    [FunctionalFact]
    public async Task GivenTheSameIsbnTwice_WhenCreatingABook_ThenTheSecondIsAConflict()
    {
        // Given
        var isbn = UniqueIsbn();

        var payload = new { title = "Duplicate", isbn, author = "Author", totalCopies = 1 };

        await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", payload);

        // When
        var second = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", payload);

        // Then
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(BookMessages.IsbnAlreadyRegistered, second.RawBody);
    }

    [FunctionalFact]
    public async Task GivenPunctuationDifferences_WhenCreatingTheSameIsbnTwice_ThenTheSecondIsStillAConflict()
    {
        // Given a book registered without separators
        var isbn = UniqueIsbn();

        await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "Normalised", isbn, author = "Author", totalCopies = 1
        });

        // When the same ISBN arrives hyphenated
        var hyphenated = $"{isbn[..3]}-{isbn[3]}-{isbn[4..9]}-{isbn[9..12]}-{isbn[12]}";

        var second = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "Normalised Again", isbn = hyphenated, author = "Author", totalCopies = 1
        });

        // Then - uniqueness is about books, not about strings
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenAnUnknownBook_WhenReadingIt_ThenItIsANotFoundProblem()
    {
        // When
        var response = await Gateway.GetAsync<DataOutput<BookOutput>>($"/books/{Guid.NewGuid()}");

        // Then
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(BookMessages.BookNotFound, response.RawBody);
    }

    [FunctionalFact]
    public async Task GivenARequestWithOneField_WhenPatchingABook_ThenTheOthersAreUntouched()
    {
        // Given
        var book = await CreateBookAsync(totalCopies: 3, title: "Original Title");

        // When
        var response = await Gateway.PatchAsync<DataOutput<BookCommandOutput>>(
            $"/books/{book.Id}", new { title = "Patched Title" });

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Patched Title", response.Body!.Data!.Title);
        Assert.Equal(book.Author, response.Body.Data.Author);
        Assert.Equal(3, response.Body.Data.TotalCopies);
    }

    [FunctionalFact]
    public async Task GivenCopiesOnLoan_WhenLoweringTheTotalBelowThem_ThenItIsUnprocessable()
    {
        // Given a book with both copies out
        var book = await CreateBookAsync(totalCopies: 2);
        var firstReader = await CreateUserAsync();
        var secondReader = await CreateUserAsync();

        await CreateLoanAsync(book.Id, firstReader.Id);
        await CreateLoanAsync(book.Id, secondReader.Id);

        // When
        var response = await Gateway.PatchAsync<DataOutput<BookCommandOutput>>(
            $"/books/{book.Id}", new { totalCopies = 1 });

        // Then - 422: the request is well-formed and collides with no other record; a business rule
        // refuses the value itself
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(BookMessages.TotalCopiesBelowActiveLoans, response.RawBody);
    }

    [FunctionalFact]
    public async Task GivenAvailabilityWasRead_WhenALoanIsMade_ThenTheCachedFigureIsRefreshed()
    {
        // Given a book whose availability has been read, and therefore cached
        var book = await CreateBookAsync(totalCopies: 2);
        var reader = await CreateUserAsync();

        var beforeLoan = await ReadAvailabilityAsync(book.Id);

        Assert.Equal(2, beforeLoan.Body!.Data!.AvailableCopies);
        Assert.NotNull(await Cache.GetAsync<BookAvailabilityOutput>(CacheKeys.Availability(book.Id)));

        // When a copy is lent
        var loan = await CreateLoanAsync(book.Id, reader.Id);

        Assert.Equal(HttpStatusCode.Created, loan.StatusCode);

        // Then - the write dropped the entry, so the next read is a miss that goes to PostgreSQL and
        // returns the new figure rather than the cached one
        var afterLoan = await ReadAvailabilityAsync(book.Id);

        Assert.Equal(1, afterLoan.Body!.Data!.AvailableCopies);
        Assert.Equal(1, afterLoan.Body.Data.ActiveLoans);
    }

    [FunctionalFact]
    public async Task GivenAvailabilityWasRead_WhenTheLoanIsReturned_ThenTheCachedFigureIsRefreshedAgain()
    {
        // Given
        var book = await CreateBookAsync(totalCopies: 1);
        var reader = await CreateUserAsync();
        var loan = await CreateLoanAsync(book.Id, reader.Id);

        var whileOut = await ReadAvailabilityAsync(book.Id);

        Assert.Equal(0, whileOut.Body!.Data!.AvailableCopies);
        Assert.False(whileOut.Body.Data.CanBeLoaned);

        // When
        await Gateway.PostAsync<DataOutput<LoanCommandOutput>>(
            $"/loans/{loan.Body!.Data!.Id}/return");

        // Then
        var afterReturn = await ReadAvailabilityAsync(book.Id);

        Assert.Equal(1, afterReturn.Body!.Data!.AvailableCopies);
        Assert.True(afterReturn.Body.Data.CanBeLoaned);
    }

    [FunctionalFact]
    public async Task GivenACachedCatalogPage_WhenABookIsAdded_ThenTheListingIsRefreshed()
    {
        // Given a filtered listing that has been read, and therefore cached
        var marker = $"marker{Guid.NewGuid():N}";

        var before = await Gateway.GetAsync<PaginatedOutput<BookOutput>>($"/books?title={marker}");

        Assert.Empty(before.Body!.Data!);

        // When a book matching that filter is added
        await CreateBookAsync(title: $"A {marker} title");

        // Then - the create dropped the whole catalogue prefix, so the same query now finds it
        var after = await Gateway.GetAsync<PaginatedOutput<BookOutput>>($"/books?title={marker}");

        Assert.Single(after.Body!.Data!);
    }

    [FunctionalFact]
    public async Task GivenAnOutOfRangePageSize_WhenListing_ThenItIsRefused()
    {
        // When
        var response = await Gateway.GetAsync<PaginatedOutput<BookOutput>>("/books?pageSize=100000");

        // Then - the bound that stops one request materialising the whole catalogue
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(PaginationMessages.InvalidPageSize, response.RawBody);
    }

    [FunctionalFact]
    public async Task GivenAUserWithADuplicateEmail_WhenCreating_ThenItIsAConflict()
    {
        // Given
        var email = $"duplicate-{Guid.NewGuid():N}@test.local";

        await Gateway.PostAsync<DataOutput<CreateUserCommandOutput>>(
            "/users", new { name = "First", email });

        // When - the same address, differently cased
        var second = await Gateway.PostAsync<DataOutput<CreateUserCommandOutput>>(
            "/users", new { name = "Second", email = email.ToUpperInvariant() });

        // Then
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains(UserMessages.EmailAlreadyRegistered, second.RawBody);
    }
}
