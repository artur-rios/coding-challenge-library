using Library.Data.Configuration;
using Library.Domain.Enums;
using Library.Shared.Loans;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Library.Data.Loans;

/// <summary>
///     PostgreSQL implementation of <see cref="ILoanTransitionWriter" />: one conditional
///     <c>UPDATE</c> whose <c>WHERE</c> carries the state the loan is expected to be in.
/// </summary>
public class LoanTransitionWriter(AppDbContext context) : ILoanTransitionWriter
{
    /// <inheritdoc />
    public async Task<bool> TryTransitionAsync(
        long loanId, LoanStatus from, LoanStatus to, DateTime timestamp, CancellationToken ct = default)
    {
        // The timestamp column belongs to the destination state, and the check constraint on the
        // table requires exactly the one that matches - so a transition writes its own column and
        // leaves the other null.
        var timestampColumn = to switch
        {
            LoanStatus.Returned => "returned_at",
            LoanStatus.Cancelled => "cancelled_at",
            _ => throw new ArgumentOutOfRangeException(
                nameof(to), to, "Only Returned and Cancelled are reachable transitions.")
        };

        var affected = await context.Database.ExecuteSqlRawAsync(
            $"""
             UPDATE "{AppDbContext.Schema}"."loan"
             SET status = @to,
                 {timestampColumn} = @timestamp
             WHERE id = @loanId
               AND status = @from
             """,
            [
                new NpgsqlParameter("to", (int)to),
                new NpgsqlParameter("timestamp", timestamp),
                new NpgsqlParameter("loanId", loanId),
                new NpgsqlParameter("from", (int)from)
            ],
            ct);

        return affected == 1;
    }
}
