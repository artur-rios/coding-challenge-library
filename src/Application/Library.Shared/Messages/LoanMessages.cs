namespace Library.Shared.Messages;

/// <summary>Canonical messages produced by the lending use cases.</summary>
public static class LoanMessages
{
    /// <summary>A copy was lent.</summary>
    public const string LoanCreatedSuccessfully = "Loan created successfully.";

    /// <summary>The copy came back.</summary>
    public const string LoanReturnedSuccessfully = "Loan returned successfully.";

    /// <summary>The loan was cancelled and kept.</summary>
    public const string LoanCancelledSuccessfully = "Loan cancelled successfully. The record is preserved.";

    /// <summary>No loan holds that identifier.</summary>
    public const string LoanNotFound = "Loan not found.";

    /// <summary>
    ///     Every copy is out. The one refusal in this API that is a normal outcome rather than a
    ///     mistake, and the one the concurrency test asserts on.
    /// </summary>
    public const string NoCopiesAvailable = "No copies of this book are currently available.";

    /// <summary>The book exists but has been withdrawn from lending.</summary>
    public const string BookNotAvailableForLoan = "This book is not available for lending.";

    /// <summary>The reader already holds a copy of this title.</summary>
    public const string UserAlreadyHasCopy = "This user already has an active loan for this book.";

    /// <summary>The loan has already been returned, so there is nothing to return.</summary>
    public const string LoanAlreadyReturned = "This loan has already been returned.";

    /// <summary>The loan was cancelled, so it can be neither returned nor cancelled again.</summary>
    public const string LoanAlreadyCancelled = "This loan has already been cancelled.";

    /// <summary>The <c>Idempotency-Key</c> header was missing or empty.</summary>
    public const string IdempotencyKeyRequired = "The Idempotency-Key header is required for this operation.";

    /// <summary>
    ///     The key has been used before, with a different request body. Answering with the first
    ///     response would quietly do something the caller did not ask for, so this is refused instead.
    /// </summary>
    public const string IdempotencyKeyReused =
        "This Idempotency-Key was already used with a different request.";

    /// <summary>
    ///     Two requests carrying the same key arrived close enough together that the first had not
    ///     committed when the second tried to record itself.
    /// </summary>
    /// <remarks>
    ///     The second is refused rather than allowed to proceed, which is the whole point: it must not
    ///     produce a second loan. It is told to retry, and a retry a moment later finds the stored
    ///     response and is answered with it.
    /// </remarks>
    public const string IdempotentRequestInFlight =
        "A request with this Idempotency-Key is currently in flight. Retry shortly.";

    /// <summary>The key is longer than the column that stores it.</summary>
    public const string IdempotencyKeyTooLong = "The Idempotency-Key header must be at most 200 characters.";

    /// <summary>The loan period requested is outside what the library allows.</summary>
    public const string LoanPeriodOutOfRange = "The loan period must be between 1 and 90 days.";

    /// <summary>The book identifier was not supplied.</summary>
    public const string BookIdRequired = "Book id is required.";

    /// <summary>The reader identifier was not supplied.</summary>
    public const string UserIdRequired = "User id is required.";

    /// <summary>
    ///     The availability count and the loan rows disagree - an invariant this API maintains, so
    ///     seeing it means something is wrong rather than merely contended.
    /// </summary>
    public const string AvailabilityInconsistent =
        "Availability could not be updated consistently. The operation was rolled back.";
}
