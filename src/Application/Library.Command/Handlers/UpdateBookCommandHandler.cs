using FluentValidation;
using Library.Command.Input;
using Library.Command.Output;
using Library.Data.Relational.Interfaces;
using Library.Data.Relational.Repositories;
using Library.Data.Relational.Transactions;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Mediator.Command.Interfaces;
using Library.Output;
using Library.Shared.Auditing;
using Library.Shared.Availability;
using Library.Shared.Caching;
using Library.Shared.Catalog;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace Library.Command.Handlers;

/// <summary>
///     Changes a book's details. Metadata is written through the tracked entity, so the optimistic
///     concurrency stamp guards it; the copy count is written through the atomic conditional update,
///     because that number is shared with every loan in flight.
/// </summary>
/// <remarks>
///     The split is the interesting part of this handler. Two librarians renaming the same book is a
///     lost update and should be refused - that is what the stamp is for. A librarian adding two
///     copies while a reader borrows one is not a conflict at all: both changes are correct and both
///     must apply. Guarding the count with the same stamp would refuse one of them for no reason,
///     which is why the count never travels through the tracked entity.
/// </remarks>
public class UpdateBookCommandHandler(
    IValidator<UpdateBookCommand> validator,
    IAsyncRepository<Book> bookWriter,
    IBookCopyReserver copyReserver,
    IAuditEventWriter auditWriter,
    IAsyncUnitOfWork unitOfWork,
    ICacheStore cache,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<UpdateBookCommand, BookCommandOutput>
{
    public async Task<DataOutput<BookCommandOutput?>> HandleAsync(
        UpdateBookCommand command, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(command, ct);

        if (!validation.IsValid)
        {
            return DataOutput<BookCommandOutput?>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var book = await bookWriter.Query().FirstOrDefaultAsync(entity => entity.PublicId == command.Id, ct);

        if (book is null)
        {
            return DataOutput<BookCommandOutput?>.New.WithError(BookMessages.BookNotFound);
        }

        var before = new { book.Title, book.Isbn, book.Author, book.TotalCopies };

        var result = await unitOfWork.ExecuteInTransactionAsync<BookCommandOutput>(async () =>
        {
            var output = DataOutput<BookCommandOutput?>.New;

            // Only the fields the caller supplied are assigned, so EF sends an UPDATE naming those
            // columns alone. That is also why the stale copy counts on the tracked entity are
            // harmless: they are never marked modified, so they are never written back over the
            // atomic update below.
            if (command.Title is not null)
            {
                book.Title = command.Title.Trim();
            }

            if (command.Author is not null)
            {
                book.Author = command.Author.Trim();
            }

            if (command.Isbn is not null)
            {
                book.Isbn = Isbn.Normalize(command.Isbn);
            }

            book.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            var update = await bookWriter.UpdateAsync(book, ct);

            if (!update.Success)
            {
                return output.WithErrors(update.Errors.Select(TranslateIsbnConflict));
            }

            if (command.TotalCopies is { } totalCopies &&
                !await copyReserver.TryAdjustTotalCopiesAsync(book.Id, totalCopies, ct))
            {
                // The only way the conditional update matches no row: the new total is below the
                // number of copies currently out on loan. The metadata written a moment ago is rolled
                // back with this refusal rather than left half-applied.
                return output.WithError(BookMessages.TotalCopiesBelowActiveLoans);
            }

            await auditWriter.WriteAsync(
                AuditEntityTypes.Book, book.PublicId, AuditActions.BookUpdated,
                new
                {
                    Before = before,
                    After = new
                    {
                        book.Title,
                        book.Isbn,
                        book.Author,
                        TotalCopies = command.TotalCopies ?? before.TotalCopies
                    }
                },
                ct);

            // Re-read rather than reported from memory: the copy counts were changed by a statement
            // EF knows nothing about, so the tracked entity's copy of them is stale by construction.
            var refreshed = await bookWriter.Query().AsNoTracking()
                .FirstOrDefaultAsync(entity => entity.PublicId == command.Id, ct);

            if (refreshed is null)
            {
                return output.WithError(BookMessages.BookNotFound);
            }

            return output
                .WithData(new BookCommandOutput
                {
                    Id = refreshed.PublicId,
                    Title = refreshed.Title,
                    Isbn = refreshed.Isbn,
                    Author = refreshed.Author,
                    TotalCopies = refreshed.TotalCopies,
                    AvailableCopies = refreshed.AvailableCopies,
                    IsActive = refreshed.IsActive,
                    CreatedAt = refreshed.CreatedAt,
                    UpdatedAt = refreshed.UpdatedAt
                })
                .WithMessage(BookMessages.BookUpdatedSuccessfully);
        }, ct: ct);

        if (result.Success)
        {
            // Both entries: the book's own availability, and every page of the listing, whose keys
            // carry filters this handler cannot enumerate.
            await cache.RemoveAsync(CacheKeys.Availability(book.PublicId), ct);
            await cache.RemoveByPrefixAsync(CacheKeys.CatalogPrefix, ct);
        }

        return result;
    }

    private static string TranslateIsbnConflict(string error) =>
        error == RelationalErrors.UniqueViolationMessage ? BookMessages.IsbnAlreadyRegistered : error;
}
