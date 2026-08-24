using System.ComponentModel.DataAnnotations;

namespace Library.Data.Relational.Entities;

/// <summary>
///     Base class for entities that participate in optimistic concurrency checks. The
///     <see cref="ConcurrencyStamp" /> is regenerated on every update by the context, so a stale
///     value makes the update affect no rows and EF Core raises a concurrency conflict.
/// </summary>
/// <remarks>
///     A GUID column rather than <c>rowversion</c> on purpose: <c>rowversion</c> is a SQL Server
///     feature and this API runs on PostgreSQL. The alternative on PostgreSQL is the system column
///     <c>xmin</c>; a stamp the application owns was preferred because it is visible in the model,
///     survives a provider change, and can be asserted on in tests without reaching for provider
///     internals.
/// </remarks>
public abstract class VersionedEntity : Entity
{
    /// <summary>Optimistic concurrency token. Regenerated whenever the entity is updated.</summary>
    [ConcurrencyCheck]
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
