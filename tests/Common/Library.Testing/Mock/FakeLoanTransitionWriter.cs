using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Shared.Loans;

namespace Library.Testing.Mock;

/// <summary>
///     In-memory stand-in for the loan's conditional state transition, applying the same
///     expected-state condition to the fake repository's rows.
/// </summary>
public class FakeLoanTransitionWriter(AsyncFakeRepository<Loan> loans) : ILoanTransitionWriter
{
    /// <summary>When set, the next transition refuses - the "somebody got there first" branch.</summary>
    public bool NextTransitionFails { get; set; }

    public Task<bool> TryTransitionAsync(
        long loanId, LoanStatus from, LoanStatus to, DateTime timestamp, CancellationToken ct = default)
    {
        if (NextTransitionFails)
        {
            NextTransitionFails = false;

            return Task.FromResult(false);
        }

        var loan = loans.Items.FirstOrDefault(entity => entity.Id == loanId);

        if (loan is null || loan.Status != from)
        {
            return Task.FromResult(false);
        }

        loan.Status = to;

        if (to == LoanStatus.Returned)
        {
            loan.ReturnedAt = timestamp;
        }
        else if (to == LoanStatus.Cancelled)
        {
            loan.CancelledAt = timestamp;
        }

        return Task.FromResult(true);
    }
}
