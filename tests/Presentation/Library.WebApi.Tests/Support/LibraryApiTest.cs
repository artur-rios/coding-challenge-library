using System.Net;
using Library.Command.Output;
using Library.Output;
using Library.Query.Output;
using Library.Testing.Containers;
using Library.Testing.Functional;
using Library.Testing.Http;

namespace Library.WebApi.Tests.Support;

/// <summary>
///     Base for the functional suite: boots the API against the shared containers and provides the
///     seeding helpers every class needs.
/// </summary>
/// <param name="containers">The shared PostgreSQL and Redis containers.</param>
public abstract class LibraryApiTest(LibraryContainers containers) : WebApiTest<Program>
{
    /// <summary>The shared containers, for assertions made directly against the database.</summary>
    protected LibraryContainers Containers { get; } = containers;

    /// <summary>Creates a book through the API and returns it.</summary>
    protected async Task<BookCommandOutput> CreateBookAsync(int totalCopies = 1, string? title = null)
    {
        var response = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = title ?? $"Title {Guid.NewGuid():N}",
            isbn = UniqueIsbn(),
            author = "Author",
            totalCopies
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return response.Body!.Data!;
    }

    /// <summary>Creates a reader through the API and returns it.</summary>
    protected async Task<CreateUserCommandOutput> CreateUserAsync()
    {
        var response = await Gateway.PostAsync<DataOutput<CreateUserCommandOutput>>("/users", new
        {
            name = "Reader",
            email = $"reader-{Guid.NewGuid():N}@test.local"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return response.Body!.Data!;
    }

    /// <summary>Creates a loan through the API, carrying the required idempotency key.</summary>
    protected Task<HttpOutput<DataOutput<LoanCommandOutput>>> CreateLoanAsync(
        Guid bookId, Guid userId, string? idempotencyKey = null, int? loanDays = null,
        HttpGateway? gateway = null)
    {
        object payload = loanDays is null
            ? new { bookId, userId }
            : new { bookId, userId, loanDays = loanDays.Value };

        return (gateway ?? Gateway).PostAsync<DataOutput<LoanCommandOutput>>(
            "/loans",
            payload,
            new Dictionary<string, string>
            {
                ["Idempotency-Key"] = idempotencyKey ?? Guid.NewGuid().ToString()
            });
    }

    /// <summary>Reads a book's availability through the API.</summary>
    protected Task<HttpOutput<DataOutput<BookAvailabilityOutput>>> ReadAvailabilityAsync(Guid bookId) =>
        Gateway.GetAsync<DataOutput<BookAvailabilityOutput>>($"/books/{bookId}/availability");

    /// <summary>
    ///     Reads a book's available-copy count straight from PostgreSQL.
    /// </summary>
    /// <remarks>
    ///     Straight from the database rather than through the availability endpoint, because that
    ///     endpoint reads through Redis and may answer with a cached figure. A test asserting that
    ///     exactly one copy was taken has to ask the source of truth.
    /// </remarks>
    protected async Task<int> ReadAvailableCopiesAsync(Guid bookId)
    {
        await using var context = Containers.CreateContext();

        return await Task.FromResult(
            context.Books.Where(book => book.PublicId == bookId)
                .Select(book => book.AvailableCopies)
                .First());
    }

    /// <summary>A valid, unique ISBN-13, so the catalogue's unique index does not fire between tests.</summary>
    protected static string UniqueIsbn()
    {
        var digits = Random.Shared.NextInt64(0, 999_999_999_999L).ToString("D12");

        var checksum = digits.Select((character, index) => (character - '0') * (index % 2 == 0 ? 1 : 3)).Sum();

        return digits + (10 - checksum % 10) % 10;
    }
}
