using System.Data;
using Library.Data.Relational.Configuration;
using Library.Data.Relational.Repositories;
using Library.Output;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Library.Data.Relational.Transactions;

/// <summary>
///     Entity Framework Core implementation of <see cref="IAsyncUnitOfWork" />. Repository saves
///     issued within the delegate flush but do not commit until the transaction commits.
/// </summary>
/// <param name="context">The application's <see cref="BaseDbContext" />.</param>
public class EfUnitOfWork(BaseDbContext context) : IAsyncUnitOfWork
{
    /// <inheritdoc />
    public async Task<DataOutput<TResult?>> ExecuteInTransactionAsync<TResult>(
        Func<Task<DataOutput<TResult?>>> work,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken ct = default)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(isolationLevel, ct);

        try
        {
            var result = await work();

            if (result.Success)
            {
                await transaction.CommitAsync(ct);
            }
            else
            {
                await RollbackQuietlyAsync(transaction);

                // Anything the delegate staged but did not save would otherwise stay in the change
                // tracker and be written by the next SaveChanges on this scope's context - after the
                // transaction that was meant to contain it has gone.
                context.ChangeTracker.Clear();
            }

            return result;
        }
        catch (Exception exception)
        {
            await RollbackQuietlyAsync(transaction);

            context.ChangeTracker.Clear();

            if (exception is OperationCanceledException)
            {
                throw;
            }

            return DataOutput<TResult?>.New.WithError(RelationalErrors.Describe(exception));
        }
    }

    /// <inheritdoc />
    public async Task<IDbTransactionHandle> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default) =>
        new EfTransactionHandle(await context.Database.BeginTransactionAsync(isolationLevel, ct));

    // Rollback must never mask the failure that triggered it: it runs untied to the caller's token
    // (which may already be canceled, making RollbackAsync throw before it rolls anything back) and
    // swallows its own errors. Disposing the transaction rolls back whatever is left.
    private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Already rolled back, or the connection is gone. Dispose completes the cleanup.
        }
    }

    private sealed class EfTransactionHandle(IDbContextTransaction transaction) : IDbTransactionHandle
    {
        public Task CommitAsync(CancellationToken ct = default) => transaction.CommitAsync(ct);

        public Task RollbackAsync(CancellationToken ct = default) => transaction.RollbackAsync(ct);

        public void Dispose() => transaction.Dispose();

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
