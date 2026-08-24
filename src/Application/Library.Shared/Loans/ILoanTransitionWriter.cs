using Library.Domain.Enums;

namespace Library.Shared.Loans;

/// <summary>
///     Moves a loan from one state to another as a single conditional update.
/// </summary>
/// <remarks>
///     The same reasoning as <c>IBookCopyReserver</c>, applied to the loan row. Reading a loan, seeing
///     it is <see cref="LoanStatus.Active" /> and then writing <see cref="LoanStatus.Returned" />
///     leaves a window in which a second request does the same - and the copy is then put back on the
///     shelf twice, so the library believes it owns a copy it does not have. Making the expected
///     current state part of the <c>WHERE</c> clause means only one of the two updates finds a row to
///     change, and the other is told the loan had already moved on.
/// </remarks>
public interface ILoanTransitionWriter
{
    /// <summary>
    ///     Moves the loan from <paramref name="from" /> to <paramref name="to" />, stamping
    ///     <paramref name="timestamp" /> on the column that belongs to the destination state.
    /// </summary>
    /// <returns><c>true</c> when the loan was in the expected state and moved; <c>false</c> otherwise.</returns>
    Task<bool> TryTransitionAsync(
        long loanId, LoanStatus from, LoanStatus to, DateTime timestamp, CancellationToken ct = default);
}
