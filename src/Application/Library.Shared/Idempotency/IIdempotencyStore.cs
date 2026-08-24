namespace Library.Shared.Idempotency;

/// <summary>
///     Remembers what a given <c>Idempotency-Key</c> already produced, so a repeat of the request can
///     be answered instead of executed.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    ///     Returns what the key already produced, or <c>null</c> when it has never been seen.
    /// </summary>
    Task<StoredIdempotentResponse?> FindAsync(string key, CancellationToken ct = default);

    /// <summary>
    ///     Records the response a key produced, as part of the caller's current transaction rather
    ///     than in one of its own.
    /// </summary>
    /// <remarks>
    ///     The write is staged into the caller's unit of work deliberately: committing the loan and
    ///     the key together is what closes the window in which a retry arriving between two separate
    ///     commits would create a second loan.
    /// </remarks>
    Task SaveAsync(StoredIdempotentResponse response, CancellationToken ct = default);
}
