using Library.Command.Input;
using Library.Command.Output;
using Library.Data.Relational.Interfaces;
using Library.Data.Relational.Transactions;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Mediator.Command.Interfaces;
using Library.Output;
using Library.Shared.Auditing;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace Library.Command.Handlers;

/// <summary>
///     Withdraws a book from lending. This is what <c>DELETE /books/{id}</c> maps to, and the mapping
///     is the decision worth stating: a book with loans has a history, and deleting the row would take
///     that history with it - so the endpoint deactivates and says so, rather than either erasing
///     quietly or refusing outright.
/// </summary>
/// <remarks>
///     A book with copies still out is refused (409) rather than deactivated. Deactivation is the
///     library saying it no longer lends this title; doing that while readers are holding copies would
///     leave those loans pointing at a title the catalogue has closed, and the return that follows
///     would put a copy back on a shelf that no longer exists as far as the catalogue is concerned.
///     Returning or cancelling first is a small ask and keeps the states coherent.
/// </remarks>
public class DeactivateBookCommandHandler(
    IAsyncRepository<Book> bookWriter,
    IAsyncReadOnlyRepository<Loan> loanReader,
    IAuditEventWriter auditWriter,
    IAsyncUnitOfWork unitOfWork,
    ICacheStore cache,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<DeactivateBookCommand, DeactivateBookCommandOutput>
{
    public async Task<DataOutput<DeactivateBookCommandOutput?>> HandleAsync(
        DeactivateBookCommand command, CancellationToken ct = default)
    {
        var book = await bookWriter.Query().FirstOrDefaultAsync(entity => entity.PublicId == command.Id, ct);

        if (book is null)
        {
            return DataOutput<DeactivateBookCommandOutput?>.New.WithError(BookMessages.BookNotFound);
        }

        var loanCount = await loanReader.Query().CountAsync(loan => loan.BookId == book.Id, ct);

        if (!book.IsActive)
        {
            // Already done. Answered as a success with a distinct message rather than as a conflict:
            // a caller repeating the request has the outcome they asked for, and there is nothing for
            // them to fix.
            return DataOutput<DeactivateBookCommandOutput?>.New
                .WithData(new DeactivateBookCommandOutput
                {
                    Id = book.PublicId,
                    IsActive = false,
                    AlreadyInactive = true,
                    PreservedLoanCount = loanCount
                })
                .WithMessage(BookMessages.BookAlreadyInactive);
        }

        var activeLoans = await loanReader.Query()
            .CountAsync(loan => loan.BookId == book.Id && loan.Status == LoanStatus.Active, ct);

        if (activeLoans > 0)
        {
            return DataOutput<DeactivateBookCommandOutput?>.New.WithError(BookMessages.BookHasActiveLoans);
        }

        var result = await unitOfWork.ExecuteInTransactionAsync<DeactivateBookCommandOutput>(async () =>
        {
            var output = DataOutput<DeactivateBookCommandOutput?>.New;

            book.IsActive = false;
            book.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

            var update = await bookWriter.UpdateAsync(book, ct);

            if (!update.Success)
            {
                return output.WithErrors(update.Errors);
            }

            await auditWriter.WriteAsync(
                AuditEntityTypes.Book, book.PublicId, AuditActions.BookDeactivated,
                new
                {
                    book.Title,
                    book.Isbn,
                    // Recorded on the entry itself, because this number is the reason the row was
                    // kept: an auditor reading the trail can see what deactivation preserved.
                    PreservedLoanCount = loanCount
                },
                ct);

            return output
                .WithData(new DeactivateBookCommandOutput
                {
                    Id = book.PublicId,
                    IsActive = false,
                    AlreadyInactive = false,
                    PreservedLoanCount = loanCount
                })
                .WithMessage(BookMessages.BookDeactivatedSuccessfully);
        }, ct: ct);

        if (result.Success)
        {
            await cache.RemoveAsync(CacheKeys.Availability(book.PublicId), ct);
            await cache.RemoveByPrefixAsync(CacheKeys.CatalogPrefix, ct);
        }

        return result;
    }
}
