using Library.Data.Relational.Entities;
using Library.Output;

namespace Library.Data.Relational.Interfaces;

/// <summary>Full asynchronous read/write repository contract for entities of type <typeparamref name="T" />.</summary>
/// <typeparam name="T">The entity type, must derive from <see cref="Entity" />.</typeparam>
public interface IAsyncRepository<T> : IAsyncReadOnlyRepository<T> where T : Entity
{
    /// <summary>Persists a new entity and returns its generated identifier.</summary>
    Task<DataOutput<long>> CreateAsync(T entity, CancellationToken ct = default);

    /// <summary>Persists multiple new entities and returns their generated identifiers.</summary>
    Task<DataOutput<IEnumerable<long>>> CreateRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

    /// <summary>Applies changes to an existing entity.</summary>
    Task<DataOutput<T>> UpdateAsync(T entity, CancellationToken ct = default);

    /// <summary>Removes an entity and returns its identifier.</summary>
    Task<DataOutput<long>> DeleteAsync(T entity, CancellationToken ct = default);
}
