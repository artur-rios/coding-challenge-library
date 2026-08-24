using System.Text.Json;
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
using Library.Shared.Idempotency;
using Library.Shared.Messages;
using Library.Shared.Telemetry;
using Library.WebApi.Core.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Library.Command.Handlers;

/// <summary>
///     Lends one copy of a book to a reader. This is the handler the challenge is really about, and
///     three separate guarantees meet in it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Concurrency.</b> The copy is taken by an atomic conditional <c>UPDATE</c> - see
///         <see cref="IBookCopyReserver" /> - whose <c>WHERE</c> clause is the rule itself. Two
///         requests for the last copy are serialised by PostgreSQL's row lock: one finds the row still
///         satisfying <c>available_copies &gt; 0</c> and takes it, the other finds it no longer does
///         and is told so. Nothing is read, decided in memory, and written back, so there is no window
///         to lose. The count cannot go negative, and no second loan is created.
///     </para>
///     <para>
///         <b>Idempotency.</b> The reservation, the loan row and the idempotency record are written in
///         one transaction, so a key becomes visible at the same instant as the loan it produced. A
///         repeat of the key is answered from the store without touching availability. A repeat that
///         arrives while the first is still in flight loses the race on the unique index and is told
///         to retry rather than allowed to create a second loan.
///     </para>
///     <para>
///         <b>Auditability.</b> The audit entry is written inside the same transaction, so a loan
///         exists if and only if the trail records it.
///     </para>
/// </remarks>
public class CreateLoanCommandHandler(
    IValidator<CreateLoanCommand> validator,
    IAsyncReadOnlyRepository<Book> bookReader,
    IAsyncReadOnlyRepository<User> userReader,
    IAsyncRepository<Loan> loanWriter,
    IBookCopyReserver copyReserver,
    IIdempotencyStore idempotencyStore,
    IAuditEventWriter auditWriter,
    IAsyncUnitOfWork unitOfWork,
    ICacheStore cache,
    LibraryMetrics metrics,
    TimeProvider timeProvider,
    ILogger<CreateLoanCommandHandler> logger)
    : ICommandHandlerAsync<CreateLoanCommand, LoanCommandOutput>
{
    /// <summary>
    ///     The endpoint an idempotency key is scoped to, so the same key used against a different
    ///     operation is a conflict rather than a replay of an unrelated response.
    /// </summary>
    public const string Endpoint = "POST /loans";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<DataOutput<LoanCommandOutput?>> HandleAsync(
        CreateLoanCommand command, CancellationToken ct = default)
    {
        var startedAt = timeProvider.GetTimestamp();
        var outcome = "rejected";

        try
        {
            var result = await ExecuteAsync(command, ct);

            outcome = result.Success
                ? result.Data?.Replayed == true ? "replayed" : "created"
                : "rejected";

            return result;
        }
        finally
        {
            metrics.RecordLoanRequestDuration(
                timeProvider.GetElapsedTime(startedAt).TotalMilliseconds, outcome);
        }
    }

    private async Task<DataOutput<LoanCommandOutput?>> ExecuteAsync(
        CreateLoanCommand command, CancellationToken ct)
    {
        var output = DataOutput<LoanCommandOutput?>.New;

        var validation = await validator.ValidateAsync(command, ct);

        if (!validation.IsValid)
        {
            return output.WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var fingerprint = IdempotencyFingerprint.Of(command.FingerprintPayload());

        // Fast path, outside the transaction: a key that has already produced a loan is answered
        // without opening one. The slow path below still handles the case where this lookup misses
        // because the first request has not committed yet.
        var stored = await idempotencyStore.FindAsync(command.IdempotencyKey, ct);

        if (stored is not null)
        {
            return Replay(stored, fingerprint, output);
        }

        var book = await bookReader.Query().AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.PublicId == command.BookId, ct);

        if (book is null)
        {
            return output.WithError(BookMessages.BookNotFound);
        }

        var user = await userReader.Query().AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.PublicId == command.UserId, ct);

        if (user is null)
        {
            return output.WithError(UserMessages.UserNotFound);
        }

        // Checked here for a clear message, and enforced again by a partial unique index on
        // (book_id, user_id) over active loans - because this check alone has exactly the race the
        // rest of the handler exists to avoid.
        var alreadyHolds = await loanWriter.Query().AsNoTracking().AnyAsync(
            loan => loan.BookId == book.Id && loan.UserId == user.Id && loan.Status == LoanStatus.Active,
            ct);

        if (alreadyHolds)
        {
            return output.WithError(LoanMessages.UserAlreadyHasCopy);
        }

        // An inactive book is distinguished from an unavailable one before the reservation is
        // attempted, because the conditional update refuses both for the same reason and the caller
        // deserves to know which it was.
        if (!book.IsActive)
        {
            return output.WithError(LoanMessages.BookNotAvailableForLoan);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var loan = new Loan
        {
            PublicId = Guid.NewGuid(),
            BookId = book.Id,
            UserId = user.Id,
            Status = LoanStatus.Active,
            LoanedAt = now,
            DueAt = now.AddDays(command.LoanDays),
            IdempotencyKey = command.IdempotencyKey
        };

        var result = await unitOfWork.ExecuteInTransactionAsync<LoanCommandOutput>(async () =>
        {
            var transactional = DataOutput<LoanCommandOutput?>.New;

            // The whole concurrency guarantee is this one call. Everything after it is bookkeeping
            // that either commits with it or does not happen.
            if (!await copyReserver.TryReserveCopyAsync(book.Id, ct))
            {
                metrics.LoanRejectedUnavailable();

                logger.LogInformation(
                    "Loan refused for book {BookId} and user {UserId}: no copy available",
                    book.PublicId, user.PublicId);

                return transactional.WithError(LoanMessages.NoCopiesAvailable);
            }

            var creation = await loanWriter.CreateAsync(loan, ct);

            if (!creation.Success)
            {
                return transactional.WithErrors(creation.Errors.Select(error =>
                    error == RelationalErrors.UniqueViolationMessage
                        ? LoanMessages.UserAlreadyHasCopy
                        : error));
            }

            var availableCopies = await bookReader.Query().AsNoTracking()
                .Where(entity => entity.Id == book.Id)
                .Select(entity => entity.AvailableCopies)
                .FirstAsync(ct);

            var loanOutput = new LoanCommandOutput
            {
                Id = loan.PublicId,
                BookId = book.PublicId,
                UserId = user.PublicId,
                Status = loan.Status,
                LoanedAt = loan.LoanedAt,
                DueAt = loan.DueAt,
                AvailableCopies = availableCopies
            };

            await auditWriter.WriteAsync(
                AuditEntityTypes.Loan, loan.PublicId, AuditActions.LoanCreated,
                new
                {
                    BookId = book.PublicId,
                    book.Title,
                    UserId = user.PublicId,
                    loan.LoanedAt,
                    loan.DueAt,
                    AvailableCopiesAfter = availableCopies,
                    command.IdempotencyKey
                },
                ct);

            var saved = await SaveIdempotencyRecordAsync(command, fingerprint, loanOutput, ct);

            if (!saved.Success)
            {
                return transactional.WithErrors(saved.Errors);
            }

            return transactional
                .WithData(loanOutput)
                .WithMessage(LoanMessages.LoanCreatedSuccessfully);
        }, ct: ct);

        if (result.Success)
        {
            metrics.LoanCreated();

            logger.LogInformation(
                "Loan {LoanId} created for book {BookId} and user {UserId}, due {DueAt:o}",
                loan.PublicId, book.PublicId, user.PublicId, loan.DueAt);

            await InvalidateAsync(book.PublicId, ct);
        }

        return result;
    }

    /// <summary>
    ///     Records the key alongside the loan. A unique violation here means a second request carrying
    ///     the same key committed first; the transaction is rolled back with it, so the copy this
    ///     attempt reserved goes back on the shelf.
    /// </summary>
    private async Task<ProcessOutput> SaveIdempotencyRecordAsync(
        CreateLoanCommand command, string fingerprint, LoanCommandOutput loanOutput, CancellationToken ct)
    {
        try
        {
            await idempotencyStore.SaveAsync(
                new StoredIdempotentResponse(
                    command.IdempotencyKey,
                    Endpoint,
                    fingerprint,
                    HttpStatusCodes.Created,
                    JsonSerializer.Serialize(loanOutput, SerializerOptions)),
                ct);

            return ProcessOutput.New;
        }
        catch (Exception exception) when (RelationalErrors.IsUniqueViolation(exception))
        {
            logger.LogInformation(
                "Idempotency key {Key} was claimed by a concurrent request; this attempt is rolled back",
                command.IdempotencyKey);

            return ProcessOutput.New.WithError(LoanMessages.IdempotentRequestInFlight);
        }
    }

    /// <summary>
    ///     Answers a repeated request from the stored response, after checking that it is genuinely
    ///     the same request.
    /// </summary>
    private DataOutput<LoanCommandOutput?> Replay(
        StoredIdempotentResponse stored, string fingerprint, DataOutput<LoanCommandOutput?> output)
    {
        if (stored.Endpoint != Endpoint || stored.RequestFingerprint != fingerprint)
        {
            // Same key, different request. Replaying the first response would tell the caller a loan
            // was made that they did not ask for; refusing is the only honest answer.
            return output.WithError(LoanMessages.IdempotencyKeyReused);
        }

        var replayed = JsonSerializer.Deserialize<LoanCommandOutput>(stored.ResponseBody, SerializerOptions);

        if (replayed is null)
        {
            logger.LogError(
                "Stored idempotent response for key {Key} could not be deserialized", stored.Key);

            return output.WithError(LoanMessages.IdempotencyKeyReused);
        }

        // The payload is byte-for-byte what the first call produced; this flag is the only difference,
        // and it is what lets a client tell a fresh loan from an answered retry.
        replayed.Replayed = true;

        metrics.IdempotentReplay();

        logger.LogInformation(
            "Idempotency key {Key} replayed loan {LoanId} without creating a new one",
            stored.Key, replayed.Id);

        return output
            .WithData(replayed)
            .WithMessage(LoanMessages.LoanCreatedSuccessfully);
    }

    private async Task InvalidateAsync(Guid bookPublicId, CancellationToken ct)
    {
        await cache.RemoveAsync(CacheKeys.Availability(bookPublicId), ct);
        await cache.RemoveByPrefixAsync(CacheKeys.CatalogPrefix, ct);
    }
}
