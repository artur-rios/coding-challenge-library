using FluentValidation;
using Library.Data.Relational.Interfaces;
using Library.Domain.Entities;
using Library.Mediator.Query.Interfaces;
using Library.Output;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace Library.Query.Handlers;

/// <summary>
///     Lists the catalogue, reading through Redis. This and the availability endpoint are the two
///     cached reads: both are hit far more often than the catalogue changes, and both are cheap to
///     rebuild when a write drops them.
/// </summary>
/// <remarks>
///     The cache is a read-through with a short time to live, and every write path that can change a
///     listing drops the whole <c>library:catalog:</c> prefix. The prefix rather than the exact key,
///     because a listing is cached per filter and page and a writer cannot know which pages a given
///     book appears on. Coarse invalidation is the right trade here: the listing is one indexed query,
///     and the alternative - reasoning about which cached pages a title change affects - is the kind
///     of cleverness that eventually serves somebody a book that no longer exists.
/// </remarks>
public class ListBooksQueryHandler(
    IValidator<ListBooksQuery> validator,
    IAsyncReadOnlyRepository<Book> bookReader,
    ICacheStore cache)
    : IPaginatedQueryHandlerAsync<ListBooksQuery, BookOutput>
{
    /// <summary>
    ///     How long a cached listing is served for. Short, because PostgreSQL is the source of truth
    ///     and this is only the window in which a missed invalidation could show a stale page.
    /// </summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<PaginatedOutput<BookOutput>> HandleAsync(
        ListBooksQuery query, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(query, ct);

        if (!validation.IsValid)
        {
            return PaginatedOutput<BookOutput>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var cacheKey = CacheKeys.Catalog(
            query.Title, query.Author, query.IncludeInactive, query.PageNumber, query.PageSize);

        var cached = await cache.GetAsync<PaginatedOutput<BookOutput>>(cacheKey, ct);

        if (cached is not null)
        {
            return cached.WithMessage(BookMessages.BooksRetrievedSuccessfully);
        }

        var books = bookReader.Query().AsNoTracking();

        if (!query.IncludeInactive)
        {
            books = books.Where(entity => entity.IsActive);
        }

        // Compared case-insensitively, which PostgreSQL renders as LOWER(); the migration adds the
        // matching functional indexes so this does not turn every listing into a sequential scan.
        if (!string.IsNullOrWhiteSpace(query.Title))
        {
            var title = query.Title.Trim().ToLower();
            books = books.Where(entity => entity.Title.ToLower().Contains(title));
        }

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            var author = query.Author.Trim().ToLower();
            books = books.Where(entity => entity.Author.ToLower().Contains(author));
        }

        var projected = books.Select(entity => new BookOutput
        {
            Id = entity.PublicId,
            Title = entity.Title,
            Isbn = entity.Isbn,
            Author = entity.Author,
            TotalCopies = entity.TotalCopies,
            AvailableCopies = entity.AvailableCopies,
            IsActive = entity.IsActive,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        });

        // Ordered by title with the identifier as a tiebreaker. Titles are not unique, and PostgreSQL
        // gives no ordering guarantee between rows whose sort key ties - each page is a separate
        // query, free to break the tie differently. Without the tiebreaker, two copies of the same
        // title straddling a page boundary could appear on both pages while another book appeared on
        // neither.
        var page = await projected
            .OrderBy(book => book.Title)
            .ThenBy(book => book.Id)
            .PaginateAsync(query.PageNumber, query.PageSize, ct);

        await cache.SetAsync(cacheKey, page, CacheTtl, ct);

        return page.WithMessage(BookMessages.BooksRetrievedSuccessfully);
    }
}
