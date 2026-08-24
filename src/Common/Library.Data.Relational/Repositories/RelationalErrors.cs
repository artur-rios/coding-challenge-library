using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Relational.Repositories;

/// <summary>
///     Classification of relational failures, and the caller-safe messages that replace provider
///     text. Provider messages name constraints, indexes, columns, SQL fragments and conflicting
///     values, so they are used for classification only and are never returned to the caller. The
///     full exception is still logged by EF Core's own diagnostics, where operators can read it.
/// </summary>
public static class RelationalErrors
{
    /// <summary>Message returned when an optimistic-concurrency conflict is detected.</summary>
    public const string ConcurrencyMessage =
        "Concurrency conflict: the record was modified or removed by another process.";

    /// <summary>Message returned when a write violates a unique constraint.</summary>
    public const string UniqueViolationMessage = "Conflict: a record with the same unique value already exists.";

    /// <summary>Message returned when a write violates a non-unique integrity rule.</summary>
    public const string IntegrityViolationMessage = "Conflict: the operation violates a data-integrity rule.";

    /// <summary>Message returned when the failure is transient and the operation may be retried.</summary>
    public const string TransientMessage = "The data store is temporarily unavailable. Please retry.";

    /// <summary>Message returned when the failure has no caller-actionable classification.</summary>
    public const string GenericMessage = "A data-access error occurred.";

    /// <summary>PostgreSQL SQLSTATE for <c>unique_violation</c>.</summary>
    public const string UniqueViolationSqlState = "23505";

    /// <summary>PostgreSQL SQLSTATE for <c>serialization_failure</c>.</summary>
    public const string SerializationFailureSqlState = "40001";

    /// <summary>PostgreSQL SQLSTATE for <c>deadlock_detected</c>.</summary>
    public const string DeadlockDetectedSqlState = "40P01";

    // Markers for the remaining integrity violations: foreign key, not null, check.
    private static readonly string[] IntegrityViolationMarkers =
    [
        "foreign key",
        "check constraint",
        "not-null",
        "null value in column"
    ];

    /// <summary>
    ///     Detects a unique-constraint (duplicate-key) violation anywhere in the exception chain. This
    ///     is what tells "somebody else inserted this idempotency key first" apart from a genuine
    ///     failure, so it is a decision the loan flow depends on rather than only a message.
    /// </summary>
    public static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException db && db.SqlState == UniqueViolationSqlState)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Detects a failure PostgreSQL asks the caller to retry: a serialization failure or a
    ///     detected deadlock. Both leave the transaction rolled back and are safe to attempt again.
    /// </summary>
    public static bool IsRetryableConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException db &&
                db.SqlState is SerializationFailureSqlState or DeadlockDetectedSqlState)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Classifies a failure and returns the caller-safe message for it. Never includes provider
    ///     text.
    /// </summary>
    public static string Describe(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return ConcurrencyMessage;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException)
            {
                return TransientMessage;
            }

            if (current is not DbException db)
            {
                continue;
            }

            if (db.SqlState == UniqueViolationSqlState)
            {
                return UniqueViolationMessage;
            }

            if (db.SqlState is SerializationFailureSqlState or DeadlockDetectedSqlState || db.IsTransient)
            {
                return TransientMessage;
            }

            if (db.SqlState?.StartsWith("23", StringComparison.Ordinal) == true ||
                IntegrityViolationMarkers.Any(marker =>
                    db.Message.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            {
                return IntegrityViolationMessage;
            }
        }

        return GenericMessage;
    }
}
