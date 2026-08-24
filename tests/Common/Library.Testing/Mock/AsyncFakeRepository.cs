using Library.Data.Relational.Entities;
using Library.Data.Relational.Interfaces;
using Library.Data.Relational.Repositories;
using Library.Output;

namespace Library.Testing.Mock;

/// <summary>
///     In-memory <see cref="IAsyncRepository{T}" /> for unit tests. Entities are stored in a backing
///     list and identifiers are assigned sequentially from <c>1</c>.
/// </summary>
/// <typeparam name="T">The entity type handled by the repository.</typeparam>
public class AsyncFakeRepository<T> : IAsyncRepository<T> where T : Entity
{
    private readonly List<T> _items = [];
    private long _nextId = 1;

    /// <summary>
    ///     When set, the next write fails with this error instead of succeeding.
    /// </summary>
    /// <remarks>
    ///     Present so a test can exercise the branch where persistence refuses - a duplicate ISBN, a
    ///     duplicate e-mail - without a database. Those branches translate the message into a
    ///     domain-specific one, and that translation is worth covering.
    /// </remarks>
    public string? NextWriteFailure { get; set; }

    /// <summary>Sets <see cref="NextWriteFailure" /> to the persistence layer's duplicate-key message.</summary>
    public void FailNextWriteWithUniqueViolation() =>
        NextWriteFailure = RelationalErrors.UniqueViolationMessage;

    /// <summary>The stored entities, for assertions.</summary>
    public IReadOnlyList<T> Items => _items;

    /// <inheritdoc />
    public IQueryable<T> Query() => new TestAsyncEnumerable<T>(_items);

    /// <inheritdoc />
    public Task<DataOutput<IEnumerable<T>>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(DataOutput<IEnumerable<T>>.New.WithData(_items));
    }

    /// <inheritdoc />
    public Task<DataOutput<T?>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(DataOutput<T?>.New.WithData(_items.FirstOrDefault(item => item.Id == id)));
    }

    /// <inheritdoc />
    public Task<DataOutput<long>> CreateAsync(T entity, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (TakeFailure() is { } failure)
        {
            return Task.FromResult(DataOutput<long>.New.WithError(failure));
        }

        entity.Id = _nextId++;

        _items.Add(entity);

        return Task.FromResult(DataOutput<long>.New.WithData(entity.Id));
    }

    /// <inheritdoc />
    public Task<DataOutput<IEnumerable<long>>> CreateRangeAsync(
        IEnumerable<T> entities, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var ids = new List<long>();

        foreach (var entity in entities)
        {
            entity.Id = _nextId++;

            _items.Add(entity);
            ids.Add(entity.Id);
        }

        return Task.FromResult(DataOutput<IEnumerable<long>>.New.WithData(ids));
    }

    /// <inheritdoc />
    public Task<DataOutput<T>> UpdateAsync(T entity, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (TakeFailure() is { } failure)
        {
            return Task.FromResult(DataOutput<T>.New.WithError(failure));
        }

        var existing = _items.FirstOrDefault(item => item.Id == entity.Id);

        if (existing is null)
        {
            return Task.FromResult(DataOutput<T>.New.WithError($"Entity with Id {entity.Id} not found"));
        }

        // The handlers mutate the tracked instance and hand the same reference back, so the entity
        // is already the stored one; replacing it keeps the fake honest if a caller ever passes a
        // detached copy.
        _items[_items.IndexOf(existing)] = entity;

        return Task.FromResult(DataOutput<T>.New.WithData(entity));
    }

    /// <inheritdoc />
    public Task<DataOutput<long>> DeleteAsync(T entity, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        _items.Remove(entity);

        return Task.FromResult(DataOutput<long>.New.WithData(entity.Id));
    }

    // One-shot: the failure applies to the next write and then clears, so a test can make one call
    // fail without every later call in the same handler failing too.
    private string? TakeFailure()
    {
        var failure = NextWriteFailure;

        NextWriteFailure = null;

        return failure;
    }
}
