using Library.Domain.Entities;
using Library.Query.Handlers;
using Library.Query.Input;
using Library.Query.Input.Validation;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Library.Testing.Attributes;
using Library.Testing.Mock;

namespace Library.Query.Tests;

// Unit tests for the catalogue listing: filtering, the default exclusion of withdrawn titles,
// pagination bounds, and the read-through cache.
public class ListBooksQueryHandlerTests
{
    private static (ListBooksQueryHandler handler, AsyncFakeRepository<Book> books, FakeCacheStore cache)
        Build()
    {
        var books = new AsyncFakeRepository<Book>();
        var cache = new FakeCacheStore();

        return (new ListBooksQueryHandler(new ListBooksQueryValidator(), books, cache), books, cache);
    }

    private static async Task<Book> SeedAsync(
        AsyncFakeRepository<Book> books, string title, string author = "Author", bool isActive = true)
    {
        var book = new Book
        {
            PublicId = Guid.NewGuid(),
            Title = title,
            Author = author,
            Isbn = "9780306406157",
            TotalCopies = 1,
            AvailableCopies = 1,
            IsActive = isActive
        };

        await books.CreateAsync(book);

        return book;
    }

    [UnitFact]
    public async Task GivenNoFilters_WhenListing_ThenActiveBooksAreReturnedOrderedByTitle()
    {
        // Given
        var (handler, books, _) = Build();
        await SeedAsync(books, "Refactoring");
        await SeedAsync(books, "Clean Code");
        await SeedAsync(books, "Domain-Driven Design");

        // When
        var output = await handler.HandleAsync(new ListBooksQuery());

        // Then
        Assert.True(output.Success);
        Assert.Equal(
            ["Clean Code", "Domain-Driven Design", "Refactoring"],
            output.Data!.Select(book => book.Title));
        Assert.Contains(BookMessages.BooksRetrievedSuccessfully, output.Messages);
    }

    [UnitFact]
    public async Task GivenAWithdrawnTitle_WhenListing_ThenItIsExcludedUnlessAskedFor()
    {
        // Given
        var (handler, books, _) = Build();
        await SeedAsync(books, "Active Title");
        await SeedAsync(books, "Withdrawn Title", isActive: false);

        // When
        var withoutInactive = await handler.HandleAsync(new ListBooksQuery());
        var withInactive = await handler.HandleAsync(new ListBooksQuery { IncludeInactive = true });

        // Then - browsing shows what can be borrowed; the withdrawn title is still reachable on request
        Assert.Single(withoutInactive.Data!);
        Assert.Equal(2, withInactive.Data!.Count);
    }

    [UnitTheory]
    [InlineData("clean")]
    [InlineData("CLEAN")]
    [InlineData("Code")]
    public async Task GivenATitleFilter_WhenListing_ThenItMatchesCaseInsensitively(string filter)
    {
        // Given
        var (handler, books, _) = Build();
        await SeedAsync(books, "Clean Code");
        await SeedAsync(books, "Refactoring");

        // When
        var output = await handler.HandleAsync(new ListBooksQuery { Title = filter });

        // Then
        var book = Assert.Single(output.Data!);
        Assert.Equal("Clean Code", book.Title);
    }

    [UnitFact]
    public async Task GivenAnAuthorFilter_WhenListing_ThenOnlyThatAuthorIsReturned()
    {
        // Given
        var (handler, books, _) = Build();
        await SeedAsync(books, "Refactoring", "Martin Fowler");
        await SeedAsync(books, "Clean Code", "Robert Martin");

        // When
        var output = await handler.HandleAsync(new ListBooksQuery { Author = "fowler" });

        // Then
        var book = Assert.Single(output.Data!);
        Assert.Equal("Refactoring", book.Title);
    }

    [UnitFact]
    public async Task GivenMoreBooksThanFitOnAPage_WhenListing_ThenPaginationMetadataIsReported()
    {
        // Given
        var (handler, books, _) = Build();

        for (var index = 0; index < 5; index++)
        {
            await SeedAsync(books, $"Title {index:D2}");
        }

        // When
        var output = await handler.HandleAsync(new ListBooksQuery { PageNumber = 2, PageSize = 2 });

        // Then
        Assert.Equal(2, output.PageNumber);
        Assert.Equal(5, output.TotalItems);
        Assert.Equal(3, output.TotalPages);
        Assert.Equal(["Title 02", "Title 03"], output.Data!.Select(book => book.Title));
    }

    [UnitTheory]
    [InlineData(0, 50, PaginationMessages.InvalidPageNumber)]
    [InlineData(1, 0, PaginationMessages.InvalidPageSize)]
    [InlineData(1, 5000, PaginationMessages.InvalidPageSize)]
    public async Task GivenOutOfRangePagination_WhenListing_ThenItIsRefused(
        int pageNumber, int pageSize, string expectedError)
    {
        // Given
        var (handler, _, _) = Build();

        // When
        var output = await handler.HandleAsync(
            new ListBooksQuery { PageNumber = pageNumber, PageSize = pageSize });

        // Then - the upper bound matters as much as the lower: an unbounded page size lets one
        // request ask the database to materialise the whole catalogue
        Assert.False(output.Success);
        Assert.Contains(expectedError, output.Errors);
    }

    [UnitFact]
    public async Task GivenAMiss_WhenListing_ThenThePageIsCachedUnderItsFilterAndPage()
    {
        // Given
        var (handler, books, cache) = Build();
        await SeedAsync(books, "Clean Code");

        // When
        await handler.HandleAsync(new ListBooksQuery { Title = "clean", PageNumber = 1, PageSize = 10 });

        // Then
        Assert.Contains(CacheKeys.Catalog("clean", null, false, 1, 10), cache.WrittenKeys);
    }

    [UnitFact]
    public async Task GivenACachedPage_WhenListing_ThenItIsServedFromTheCache()
    {
        // Given a database with one book and a cached page that says otherwise
        var (handler, books, cache) = Build();
        await SeedAsync(books, "In the database");

        var key = CacheKeys.Catalog(null, null, false, 1, 50);

        cache.Seed(key, Output.PaginatedOutputFor("From the cache"));

        // When
        var output = await handler.HandleAsync(new ListBooksQuery());

        // Then
        var book = Assert.Single(output.Data!);
        Assert.Equal("From the cache", book.Title);
    }

    // Small helper kept beside the test that uses it, rather than in the shared harness: it exists to
    // build one deliberately-wrong cached page and nothing else needs it.
    private static class Output
    {
        public static global::Library.Output.PaginatedOutput<global::Library.Query.Output.BookOutput>
            PaginatedOutputFor(string title) =>
            global::Library.Output.PaginatedOutput<global::Library.Query.Output.BookOutput>.New
                .WithData([new global::Library.Query.Output.BookOutput { Id = Guid.NewGuid(), Title = title }])
                .WithPagination(1, 50, 1);
    }
}
