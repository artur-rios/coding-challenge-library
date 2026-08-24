using Library.Command.Input;
using Library.Command.Output;
using Library.Data.Relational.Interfaces;
using Library.Data.Relational.Transactions;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Mediator.Command.Interfaces;
using Library.Output;
using Library.Shared.Auditing;
using Library.Shared.Availability;
using Library.Shared.Caching;
using Library.Shared.Loans;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Library.Command.Handlers;

/// <summary>
///     Records that a lent copy came back: moves the loan to <see cref="LoanStatus.Returned" /> and
///     puts the copy back on the shelf.
/// </summary>
/// <remarks>
///     Both steps are conditional updates, and both conditions matter. The loan moves only if it is
///     still active, so two concurrent returns of the same loan cannot both succeed - which would
///     otherwise credit the shelf with two copies for one book. The shelf is credited only if that
///     would not push the available count past the total, which is an invariant rather than a race:
///     if it is ever violated the operation is rolled back and reported, because a library that
///     believes it owns copies it does not have will keep lending them.
/// </remarks>
public class ReturnLoanCommandHandler(
    IAsyncReadOnlyRepository<Loan> loanReader,
    IAsyncReadOnlyRepository<Book> bookReader,
    ILoanTransitionWriter loanTransitions,
    IBookCopyReserver copyReserver,
    IAuditEventWriter auditWriter,
    IAsyncUnitOfWork unitOfWork,
    ICacheStore cache,
    TimeProvider timeProvider,
    ILogger<ReturnLoanCommandHandler> logger)
    : ICommandHandlerAsync<ReturnLoanCommand, LoanCommandOutput>
{
    public async Task<DataOutput<LoanCommandOutput?>> HandleAsync(
        ReturnLoanCommand command, CancellationToken ct = default)
    {
        var loan = await loanReader.Query().AsNoTracking()
            .Include(entity => entity.Book)
            .Include(entity => entity.User)
            .FirstOrDefaultAsync(entity => entity.PublicId == command.Id, ct);

        if (loan is null)
        {
            return DataOutput<LoanCommandOutput?>.New.WithError(LoanMessages.LoanNotFound);
        }

        if (loan.Status is LoanStatus.Returned or LoanStatus.Cancelled)
        {
            return DataOutput<LoanCommandOutput?>.New.WithError(
                loan.Status == LoanStatus.Returned
                    ? LoanMessages.LoanAlreadyReturned
                    : LoanMessages.LoanAlreadyCancelled);
        }

        var returnedAt = timeProvider.GetUtcNow().UtcDateTime;

        var result = await unitOfWork.ExecuteInTransactionAsync<LoanCommandOutput>(async () =>
        {
            var output = DataOutput<LoanCommandOutput?>.New;

            if (!await loanTransitions.TryTransitionAsync(
                    loan.Id, LoanStatus.Active, LoanStatus.Returned, returnedAt, ct))
            {
                // The loan moved between the read above and this update - a concurrent return or
                // cancellation got there first.
                return output.WithError(LoanMessages.LoanAlreadyReturned);
            }

            if (!await copyReserver.TryReleaseCopyAsync(loan.BookId, ct))
            {
                logger.LogError(
                    "Returning loan {LoanId} would push book {BookId} above its total copies",
                    loan.PublicId, loan.Book.PublicId);

                return output.WithError(LoanMessages.AvailabilityInconsistent);
            }

            var availableCopies = await ReadAvailableCopiesAsync(loan.BookId, ct);

            await auditWriter.WriteAsync(
                AuditEntityTypes.Loan, loan.PublicId, AuditActions.LoanReturned,
                new
                {
                    BookId = loan.Book.PublicId,
                    UserId = loan.User.PublicId,
                    loan.LoanedAt,
                    loan.DueAt,
                    ReturnedAt = returnedAt,
                    // Whether the copy came back late is a business fact somebody will want from the
                    // trail without having to recompute it from two timestamps.
                    WasOverdue = returnedAt > loan.DueAt,
                    AvailableCopiesAfter = availableCopies
                },
                ct);

            return output
                .WithData(new LoanCommandOutput
                {
                    Id = loan.PublicId,
                    BookId = loan.Book.PublicId,
                    UserId = loan.User.PublicId,
                    Status = LoanStatus.Returned,
                    LoanedAt = loan.LoanedAt,
                    DueAt = loan.DueAt,
                    ReturnedAt = returnedAt,
                    AvailableCopies = availableCopies
                })
                .WithMessage(LoanMessages.LoanReturnedSuccessfully);
        }, ct: ct);

        if (result.Success)
        {
            await cache.RemoveAsync(CacheKeys.Availability(loan.Book.PublicId), ct);
            await cache.RemoveByPrefixAsync(CacheKeys.CatalogPrefix, ct);
        }

        return result;
    }

    // Read inside the transaction, after the release, so the number reported to the caller is the one
    // the commit will make visible rather than the one from before the update.
    private Task<int> ReadAvailableCopiesAsync(long bookId, CancellationToken ct) =>
        bookReader.Query().AsNoTracking()
            .Where(entity => entity.Id == bookId)
            .Select(entity => entity.AvailableCopies)
            .FirstAsync(ct);
}
