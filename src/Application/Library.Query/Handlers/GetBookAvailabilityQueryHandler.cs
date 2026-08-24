using Library.Data.Relational.Interfaces;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Mediator.Query.Interfaces;
using Library.Output;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace Library.Query.Handlers;

/// <summary>
///     Reports how many copies of one book are on the shelf, through Redis.
/// </summary>
/// <remarks>
///     <para>
///         This is the read the challenge asks to be cached, and the one where the distinction between
///         a cache and a source of truth has to be made explicitly. The answer here is advisory: it
///         may be up to <see cref="CacheTtl" /> old, and even a fresh answer describes a moment that
///         has passed by the time the caller reads it. Nothing decides whether a loan may happen from
///         this value - that decision is taken once, inside <c>POST /loans</c>, by a conditional
///         update against PostgreSQL.
///     </para>
///     <para>
///         Every write that moves the count - a loan, a return, a cancellation, a change to the total
///         - drops this key immediately after its transaction commits, so the stale window in practice
///         is the gap between the commit and the delete rather than the whole time to live. The time
///         to live exists for the case where that delete does not happen: a Redis blip, a replica
///         restarting mid-request. It bounds how wrong the cache can be when invalidation is missed.
///     </para>
/// </remarks>
public class GetBookAvailabilityQueryHandler(
    IAsyncReadOnlyRepository<Book> bookReader,
    IAsyncReadOnlyRepository<Loan> loanReader,
    ICacheStore cache,
    TimeProvider timeProvider)
    : IQueryHandlerAsync<GetBookAvailabilityQuery, BookAvailabilityOutput>
{
    /// <summary>How long a cached availability answer is served for.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<DataOutput<BookAvailabilityOutput?>> HandleAsync(
        GetBookAvailabilityQuery query, CancellationToken ct = default)
    {
        var output = DataOutput<BookAvailabilityOutput?>.New;

        var cacheKey = CacheKeys.Availability(query.BookId);

        var cached = await cache.GetAsync<BookAvailabilityOutput>(cacheKey, ct);

        if (cached is not null)
        {
            return output.WithData(cached).WithMessage(BookMessages.AvailabilityRetrievedSuccessfully);
        }

        var book = await bookReader.Query().AsNoTracking()
            .Where(entity => entity.PublicId == query.BookId)
            .Select(entity => new
            {
                entity.Id,
                entity.PublicId,
                entity.Title,
                entity.TotalCopies,
                entity.AvailableCopies,
                entity.IsActive
            })
            .FirstOrDefaultAsync(ct);

        if (book is null)
        {
            // A miss is not cached. Caching absence would need its own invalidation on book creation,
            // and the alternative - a caller who just created a book being told it does not exist -
            // is a worse failure than the extra query.
            return output.WithError(BookMessages.BookNotFound);
        }

        var activeLoans = await loanReader.Query().AsNoTracking()
            .CountAsync(loan => loan.BookId == book.Id && loan.Status == LoanStatus.Active, ct);

        var availability = new BookAvailabilityOutput
        {
            BookId = book.PublicId,
            Title = book.Title,
            TotalCopies = book.TotalCopies,
            AvailableCopies = book.AvailableCopies,
            ActiveLoans = activeLoans,
            IsActive = book.IsActive,
            CalculatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        await cache.SetAsync(cacheKey, availability, CacheTtl, ct);

        return output
            .WithData(availability)
            .WithMessage(BookMessages.AvailabilityRetrievedSuccessfully);
    }
}
