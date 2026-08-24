using Library.Data.Relational.Interfaces;
using Library.Domain.Entities;
using Library.Mediator.Query.Interfaces;
using Library.Output;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace Library.Query.Handlers;

/// <summary>
///     Reads one book by its public identifier, deactivated ones included - a caller who asks for a
///     specific book by identifier is not browsing, and hiding a withdrawn title from them would only
///     make it look deleted.
/// </summary>
public class GetBookByIdQueryHandler(IAsyncReadOnlyRepository<Book> bookReader)
    : IQueryHandlerAsync<GetBookByIdQuery, BookOutput>
{
    public async Task<DataOutput<BookOutput?>> HandleAsync(
        GetBookByIdQuery query, CancellationToken ct = default)
    {
        var output = DataOutput<BookOutput?>.New;

        // Projected in the query rather than materialised and mapped, so PostgreSQL returns the seven
        // columns the response needs instead of the whole row.
        var book = await bookReader.Query().AsNoTracking()
            .Where(entity => entity.PublicId == query.Id)
            .Select(entity => new BookOutput
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
            })
            .FirstOrDefaultAsync(ct);

        // Read straight from PostgreSQL and never from Redis. A single book by identifier is an index
        // lookup on a primary key - caching it would add an invalidation path for no measurable gain,
        // and every cache entry that exists is one that can be stale.
        return book is null
            ? output.WithError(BookMessages.BookNotFound)
            : output.WithData(book).WithMessage(BookMessages.BookRetrievedSuccessfully);
    }
}
