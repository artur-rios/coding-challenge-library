using Library.Data.Configuration;
using Library.Shared.Availability;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Library.Data.Availability;

/// <summary>
///     PostgreSQL implementation of <see cref="IBookCopyReserver" />: every change to a book's copy
///     counts is one <c>UPDATE ... WHERE</c>, and the <c>WHERE</c> clause is the business rule.
/// </summary>
/// <remarks>
///     <para>
///         Raw SQL rather than <c>ExecuteUpdateAsync</c>, for one reason: these statements read a
///         column and write a value derived from it in the same statement
///         (<c>available_copies = available_copies - 1</c>), which is what makes them atomic. Writing
///         them out keeps that visible - the correctness of this whole API rests on these four
///         statements, and they should be readable as SQL by whoever has to verify them.
///     </para>
///     <para>
///         PostgreSQL takes a row-level lock for the duration of each statement and, at
///         <c>READ COMMITTED</c>, re-evaluates the <c>WHERE</c> clause against the committed row after
///         waiting for any concurrent writer to finish. That re-evaluation is precisely why two
///         requests for the last copy cannot both succeed: the second one's <c>available_copies &gt;
///         0</c> is tested against the value the first one committed, not against the value it read
///         when it started.
///     </para>
/// </remarks>
public class BookCopyReserver(AppDbContext context) : IBookCopyReserver
{
    /// <inheritdoc />
    public async Task<bool> TryReserveCopyAsync(long bookId, CancellationToken ct = default)
    {
        // is_active is part of the condition as well as the count: a book withdrawn between the
        // handler's check and this statement must not be lent, and this is the only place where that
        // can be decided without a gap.
        var affected = await context.Database.ExecuteSqlRawAsync(
            $"""
             UPDATE "{AppDbContext.Schema}"."book"
             SET available_copies = available_copies - 1,
                 updated_at = now()
             WHERE id = @bookId
               AND is_active = TRUE
               AND available_copies > 0
             """,
            [new NpgsqlParameter("bookId", bookId)],
            ct);

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryReleaseCopyAsync(long bookId, CancellationToken ct = default)
    {
        // Bounded by the total, so a double return cannot inflate the shelf. An inactive book can
        // still take a copy back: withdrawing a title must not strand copies that are out.
        var affected = await context.Database.ExecuteSqlRawAsync(
            $"""
             UPDATE "{AppDbContext.Schema}"."book"
             SET available_copies = available_copies + 1,
                 updated_at = now()
             WHERE id = @bookId
               AND available_copies < total_copies
             """,
            [new NpgsqlParameter("bookId", bookId)],
            ct);

        return affected == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryAdjustTotalCopiesAsync(
        long bookId, int totalCopies, CancellationToken ct = default)
    {
        // The delta is computed inside the statement from the row's own current total, so this needs
        // no prior read and cannot act on a stale one. The guard refuses a new total below the number
        // of copies currently out on loan - which is exactly (total_copies - available_copies).
        var affected = await context.Database.ExecuteSqlRawAsync(
            $"""
             UPDATE "{AppDbContext.Schema}"."book"
             SET available_copies = available_copies + (@totalCopies - total_copies),
                 total_copies = @totalCopies,
                 updated_at = now()
             WHERE id = @bookId
               AND available_copies + (@totalCopies - total_copies) >= 0
             """,
            [new NpgsqlParameter("bookId", bookId), new NpgsqlParameter("totalCopies", totalCopies)],
            ct);

        return affected == 1;
    }
}
