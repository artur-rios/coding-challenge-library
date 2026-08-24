namespace Library.Data.Relational.Transactions;

/// <summary>A handle over an active database transaction with manual commit/rollback control.</summary>
public interface IDbTransactionHandle : IDisposable, IAsyncDisposable
{
    /// <summary>Commits the transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Rolls the transaction back.</summary>
    Task RollbackAsync(CancellationToken ct = default);
}
