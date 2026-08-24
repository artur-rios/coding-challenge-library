using Library.Data.Relational.Configuration;
using Library.Data.Relational.Entities;
using Library.Data.Relational.Interfaces;
using Library.Output;
using Microsoft.EntityFrameworkCore;

namespace Library.Data.Relational.Repositories;

/// <summary>
///     Entity Framework Core implementation of the repository contracts. Every write auto-saves;
///     inside an active unit-of-work transaction, saves flush without committing. Infrastructure
///     failures are returned as <see cref="DataOutput{T}" /> errors rather than thrown, so a handler
///     reports them the same way it reports a business refusal.
/// </summary>
/// <typeparam name="T">The entity type.</typeparam>
/// <param name="context">The application's <see cref="BaseDbContext" />.</param>
public class EfRepository<T>(BaseDbContext context) : IAsyncRepository<T> where T : Entity
{
    /// <summary>The tracked entity set for <typeparamref name="T" />.</summary>
    protected DbSet<T> Set => context.Set<T>();

    /// <inheritdoc />
    public IQueryable<T> Query() => Set.AsQueryable();

    /// <inheritdoc />
    public Task<DataOutput<IEnumerable<T>>> GetAllAsync(CancellationToken ct = default) =>
        GuardedAsync<IEnumerable<T>>(async () => await Set.ToListAsync(ct));

    /// <inheritdoc />
    public Task<DataOutput<T?>> GetByIdAsync(long id, CancellationToken ct = default) =>
        GuardedAsync(async () => await Set.FirstOrDefaultAsync(entity => entity.Id == id, ct));

    /// <inheritdoc />
    public Task<DataOutput<long>> CreateAsync(T entity, CancellationToken ct = default) =>
        GuardedAsync(async () =>
        {
            await Set.AddAsync(entity, ct);
            await context.SaveChangesAsync(ct);

            return entity.Id;
        });

    /// <inheritdoc />
    public Task<DataOutput<IEnumerable<long>>> CreateRangeAsync(
        IEnumerable<T> entities, CancellationToken ct = default) =>
        GuardedAsync<IEnumerable<long>>(async () =>
        {
            var list = entities.ToList();

            await Set.AddRangeAsync(list, ct);
            await context.SaveChangesAsync(ct);

            return list.Select(entity => entity.Id).ToList();
        });

    /// <inheritdoc />
    public Task<DataOutput<T>> UpdateAsync(T entity, CancellationToken ct = default) =>
        GuardedAsync(async () =>
        {
            Set.Update(entity);
            await context.SaveChangesAsync(ct);

            return entity;
        });

    /// <inheritdoc />
    public Task<DataOutput<long>> DeleteAsync(T entity, CancellationToken ct = default) =>
        GuardedAsync(async () =>
        {
            Set.Remove(entity);
            await context.SaveChangesAsync(ct);

            return entity.Id;
        });

    /// <summary>Runs an asynchronous data operation, converting failures to envelope errors.</summary>
    /// <remarks>
    ///     <see cref="OperationCanceledException" /> is rethrown rather than described: a request the
    ///     client abandoned is not a data-access error, and turning it into one would have the API
    ///     log a failure every time somebody closes a tab.
    /// </remarks>
    protected static async Task<DataOutput<TResult>> GuardedAsync<TResult>(Func<Task<TResult>> operation)
    {
        try
        {
            return DataOutput<TResult>.New.WithData(await operation());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return DataOutput<TResult>.New.WithError(RelationalErrors.Describe(exception));
        }
    }
}
