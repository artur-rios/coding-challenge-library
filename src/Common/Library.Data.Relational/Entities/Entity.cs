using System.ComponentModel.DataAnnotations.Schema;

namespace Library.Data.Relational.Entities;

/// <summary>Abstract base class for all data entities. Provides the surrogate primary key.</summary>
/// <remarks>
///     The key is a <see cref="long" /> the database generates, and it never leaves the process:
///     every entity that is addressable from outside also carries a <c>PublicId</c> GUID, which is
///     what the API paths and response bodies use. Keeping the two apart means a caller cannot
///     enumerate the catalogue by counting, and a hard delete cannot be silently re-pointed at a
///     recycled identifier.
/// </remarks>
public abstract class Entity
{
    /// <summary>The unique identifier for the entity.</summary>
    [Column(Order = 1)]
    public long Id { get; set; }
}
