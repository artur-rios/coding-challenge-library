using System.Data;
using Library.Data.Relational.Repositories;
using Library.Data.Relational.Transactions;
using Library.Output;

namespace Library.Testing.Mock;

/// <summary>
///     A unit of work with no database behind it: it runs the delegate and records what the commit
///     decision would have been.
/// </summary>
/// <remarks>
///     It cannot roll anything back - there is nothing to roll back - so a unit test asserting "the
///     write did not happen" asserts on <see cref="Committed" /> and on the fake repositories, and the
///     real rollback is covered by the integration tests against PostgreSQL. What this does verify is
///     that a handler which refuses returns a failed envelope from inside the transaction, which is
///     what the real wrapper keys its rollback off.
/// </remarks>
public class FakeUnitOfWork : IAsyncUnitOfWork
{
    /// <summary>Whether the last executed delegate would have committed.</summary>
    public bool Committed { get; private set; }

    /// <summary>How many transactions were opened.</summary>
    public int TransactionCount { get; private set; }

    /// <summary>The isolation level the last transaction was opened at.</summary>
    public IsolationLevel LastIsolationLevel { get; private set; } = IsolationLevel.Unspecified;

    public async Task<DataOutput<TResult?>> ExecuteInTransactionAsync<TResult>(
        Func<Task<DataOutput<TResult?>>> work,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        TransactionCount++;
        LastIsolationLevel = isolationLevel;

        try
        {
            var result = await work();

            Committed = result.Success;

            return result;
        }
        catch (Exception exception)
        {
            Committed = false;

            if (exception is OperationCanceledException)
            {
                throw;
            }

            // Same classification the real wrapper applies, so a handler whose audit writer throws is
            // seen by the test exactly as it would be in production.
            return DataOutput<TResult?>.New.WithError(RelationalErrors.Describe(exception));
        }
    }

    public Task<IDbTransactionHandle> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default) =>
        throw new NotSupportedException(
            "No handler uses manual transaction control; if one starts, this fake needs to grow.");
}
