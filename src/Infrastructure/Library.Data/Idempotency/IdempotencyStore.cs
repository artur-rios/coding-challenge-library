using Library.Data.Configuration;
using Library.Domain.Entities;
using Library.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Idempotency;

/// <summary>
///     PostgreSQL implementation of <see cref="IIdempotencyStore" />, writing through the same
///     <c>DbContext</c> - and therefore the same transaction - as the loan it describes.
/// </summary>
/// <param name="context">The application's context.</param>
/// <param name="options">How long a key is remembered.</param>
public class IdempotencyStore(AppDbContext context, IdempotencyOptions options) : IIdempotencyStore
{
    /// <inheritdoc />
    public async Task<StoredIdempotentResponse?> FindAsync(string key, CancellationToken ct = default)
    {
        var record = await context.IdempotencyRecords.AsNoTracking()
            .FirstOrDefaultAsync(entity => entity.Key == key, ct);

        if (record is null)
        {
            return null;
        }

        // An expired record is treated as absent rather than deleted here: deleting on a read path
        // would turn a GET-shaped operation into a write, and the sweep removes it soon enough. The
        // caller then does the work again, which is correct - a key nobody has retried for a day is
        // not a retry.
        return record.ExpiresAt <= DateTime.UtcNow
            ? null
            : new StoredIdempotentResponse(
                record.Key, record.Endpoint, record.RequestFingerprint, record.StatusCode,
                record.ResponseBody);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Deliberately unguarded: a unique-constraint violation here is meaningful - it means another
    ///     request claimed this key first - and the caller catches it to roll its own work back. A
    ///     repository that swallowed the exception into an error envelope would leave the caller's
    ///     transaction free to commit a second loan, which is the one outcome this class exists to
    ///     prevent.
    /// </remarks>
    public async Task SaveAsync(StoredIdempotentResponse response, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = response.Key,
            Endpoint = response.Endpoint,
            RequestFingerprint = response.RequestFingerprint,
            StatusCode = response.StatusCode,
            ResponseBody = response.ResponseBody,
            CreatedAt = now,
            ExpiresAt = now.Add(options.Retention)
        });

        await context.SaveChangesAsync(ct);
    }
}
