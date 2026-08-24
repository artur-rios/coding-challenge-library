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
using Library.Shared.Caching;
using Library.Shared.Catalog;
using Library.Shared.Messages;

namespace Library.Command.Handlers;

/// <summary>
///     Adds a title to the catalogue: validates the request, normalises the ISBN, writes the book and
///     its audit entry in one transaction, then drops the cached catalogue listing.
/// </summary>
public class CreateBookCommandHandler(
    IValidator<CreateBookCommand> validator,
    IAsyncRepository<Book> bookWriter,
    IAuditEventWriter auditWriter,
    IAsyncUnitOfWork unitOfWork,
    ICacheStore cache,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateBookCommand, BookCommandOutput>
{
    public async Task<DataOutput<BookCommandOutput?>> HandleAsync(
        CreateBookCommand command, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(command, ct);

        if (!validation.IsValid)
        {
            return DataOutput<BookCommandOutput?>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var book = new Book
        {
            PublicId = Guid.NewGuid(),
            Title = command.Title.Trim(),
            // Normalised, so the unique index enforces "this book is already in the catalogue"
            // rather than "this exact string is already in the catalogue".
            Isbn = Isbn.Normalize(command.Isbn),
            Author = command.Author.Trim(),
            TotalCopies = command.TotalCopies,
            // A new title is entirely on the shelf: nothing can be out on loan yet.
            AvailableCopies = command.TotalCopies,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        var result = await unitOfWork.ExecuteInTransactionAsync<BookCommandOutput>(async () =>
        {
            var output = DataOutput<BookCommandOutput?>.New;

            var creation = await bookWriter.CreateAsync(book, ct);

            if (!creation.Success)
            {
                // The repository classified the failure; the only one this command can provoke is a
                // duplicate ISBN, which deserves its own message rather than the generic conflict.
                return output.WithErrors(creation.Errors.Select(TranslateIsbnConflict));
            }

            await auditWriter.WriteAsync(
                AuditEntityTypes.Book, book.PublicId, AuditActions.BookCreated,
                new
                {
                    book.Title,
                    book.Isbn,
                    book.Author,
                    book.TotalCopies,
                    book.AvailableCopies
                },
                ct);

            return output
                .WithData(new BookCommandOutput
                {
                    Id = book.PublicId,
                    Title = book.Title,
                    Isbn = book.Isbn,
                    Author = book.Author,
                    TotalCopies = book.TotalCopies,
                    AvailableCopies = book.AvailableCopies,
                    IsActive = book.IsActive,
                    CreatedAt = book.CreatedAt,
                    UpdatedAt = book.UpdatedAt
                })
                .WithMessage(BookMessages.BookCreatedSuccessfully);
        }, ct: ct);

        if (result.Success)
        {
            // After the commit, never before: dropping the listing while the transaction could still
            // roll back would let a concurrent read repopulate the cache from a state the database
            // never reached.
            await cache.RemoveByPrefixAsync(CacheKeys.CatalogPrefix, ct);
        }

        return result;
    }

    private static string TranslateIsbnConflict(string error) =>
        error == RelationalErrors.UniqueViolationMessage ? BookMessages.IsbnAlreadyRegistered : error;
}
