using System.Data;
using Library.Output;

namespace Library.Data.Relational.Transactions;

/// <summary>Asynchronously coordinates repository operations within a single database transaction.</summary>
public interface IAsyncUnitOfWork
{
    /// <summary>
    ///     Runs <paramref name="work" /> in a transaction, committing when its envelope reports
    ///     success and rolling back when it does not.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The delegate returns the same envelope the handler will return, and the commit decision
    ///         is taken from it. That is the point: a handler that refuses halfway through - the new
    ///         total is below the copies on loan, the idempotency key was already taken - has usually
    ///         written something already, and a wrapper that commits whatever the delegate returns
    ///         would keep those partial writes. Tying the commit to the envelope means a refusal
    ///         leaves the database exactly as it found it, without the handler having to remember to
    ///         roll back.
    ///     </para>
    ///     <para>
    ///         An exception rolls back too and is returned as a classified error, so a caller sees a
    ///         message rather than a stack trace. Cancellation is rethrown after the rollback: an
    ///         abandoned request is not a failure to report.
    ///     </para>
    /// </remarks>
    Task<DataOutput<TResult?>> ExecuteInTransactionAsync<TResult>(
        Func<Task<DataOutput<TResult?>>> work,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default);

    /// <summary>Begins a transaction for manual commit/rollback control.</summary>
    /// <remarks>
    ///     The isolation level is a parameter rather than a constant because the loan flow's choice of
    ///     it is a real decision: it runs at <see cref="IsolationLevel.ReadCommitted" /> and relies on
    ///     an atomic conditional update for correctness, instead of paying for
    ///     <see cref="IsolationLevel.Serializable" /> and the retries it forces.
    /// </remarks>
    Task<IDbTransactionHandle> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default);
}
