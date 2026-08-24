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
///     Calls off a loan: moves it to <see cref="LoanStatus.Cancelled" /> and puts the copy back.
/// </summary>
/// <remarks>
///     Mechanically almost identical to a return, and deliberately kept as a separate state rather
///     than folded into one. The requirement is that a cancelled loan still shows that it existed, and
///     "returned" would be a false statement about it - the reader never brought the copy back,
///     because they never kept it. Anyone reading the history months later needs to be able to tell
///     those two apart, and a shared status with a nullable reason column would not let them.
/// </remarks>
public class CancelLoanCommandHandler(
    IAsyncReadOnlyRepository<Loan> loanReader,
    IAsyncReadOnlyRepository<Book> bookReader,
    ILoanTransitionWriter loanTransitions,
    IBookCopyReserver copyReserver,
    IAuditEventWriter auditWriter,
    IAsyncUnitOfWork unitOfWork,
    ICacheStore cache,
    TimeProvider timeProvider,
    ILogger<CancelLoanCommandHandler> logger)
    : ICommandHandlerAsync<CancelLoanCommand, LoanCommandOutput>
{
    /// <summary>Longest cancellation reason recorded on the audit entry.</summary>
    private const int MaxReasonLength = 500;

    public async Task<DataOutput<LoanCommandOutput?>> HandleAsync(
        CancelLoanCommand command, CancellationToken ct = default)
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

        var cancelledAt = timeProvider.GetUtcNow().UtcDateTime;

        var result = await unitOfWork.ExecuteInTransactionAsync<LoanCommandOutput>(async () =>
        {
            var output = DataOutput<LoanCommandOutput?>.New;

            if (!await loanTransitions.TryTransitionAsync(
                    loan.Id, LoanStatus.Active, LoanStatus.Cancelled, cancelledAt, ct))
            {
                return output.WithError(LoanMessages.LoanAlreadyCancelled);
            }

            if (!await copyReserver.TryReleaseCopyAsync(loan.BookId, ct))
            {
                logger.LogError(
                    "Cancelling loan {LoanId} would push book {BookId} above its total copies",
                    loan.PublicId, loan.Book.PublicId);

                return output.WithError(LoanMessages.AvailabilityInconsistent);
            }

            var availableCopies = await bookReader.Query().AsNoTracking()
                .Where(entity => entity.Id == loan.BookId)
                .Select(entity => entity.AvailableCopies)
                .FirstAsync(ct);

            await auditWriter.WriteAsync(
                AuditEntityTypes.Loan, loan.PublicId, AuditActions.LoanCancelled,
                new
                {
                    BookId = loan.Book.PublicId,
                    UserId = loan.User.PublicId,
                    loan.LoanedAt,
                    loan.DueAt,
                    CancelledAt = cancelledAt,
                    Reason = Truncate(command.Reason),
                    AvailableCopiesAfter = availableCopies
                },
                ct);

            return output
                .WithData(new LoanCommandOutput
                {
                    Id = loan.PublicId,
                    BookId = loan.Book.PublicId,
                    UserId = loan.User.PublicId,
                    Status = LoanStatus.Cancelled,
                    LoanedAt = loan.LoanedAt,
                    DueAt = loan.DueAt,
                    CancelledAt = cancelledAt,
                    AvailableCopies = availableCopies
                })
                .WithMessage(LoanMessages.LoanCancelledSuccessfully);
        }, ct: ct);

        if (result.Success)
        {
            await cache.RemoveAsync(CacheKeys.Availability(loan.Book.PublicId), ct);
            await cache.RemoveByPrefixAsync(CacheKeys.CatalogPrefix, ct);
        }

        return result;
    }

    // The reason is caller-supplied free text going into a long-retained table, so it is bounded here
    // rather than trusted to be short.
    private static string? Truncate(string? reason) =>
        reason is { Length: > MaxReasonLength } ? reason[..MaxReasonLength] : reason;
}
