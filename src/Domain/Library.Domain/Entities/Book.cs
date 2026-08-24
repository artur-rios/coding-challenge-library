using System.ComponentModel.DataAnnotations;
using Library.Data.Relational.Entities;

namespace Library.Domain.Entities;

/// <summary>
///     A catalogue title and the copies of it the library holds. One row per title, not per copy:
///     the copies are indistinguishable for lending purposes, so counting them is enough and keeps
///     the "is anything available" decision to a single row.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="AvailableCopies" /> is the authoritative count and PostgreSQL is its only
///         source of truth. It is never written by reading the row into memory, subtracting, and
///         saving: two replicas doing that concurrently both read 1 and both write 0, and the last
///         copy is lent twice. Every change to it goes through an atomic conditional <c>UPDATE</c>
///         whose <c>WHERE</c> clause carries the rule - see <c>IBookCopyReserver</c>.
///     </para>
///     <para>
///         The inherited <see cref="VersionedEntity.ConcurrencyStamp" /> guards the metadata instead:
///         two librarians editing the same title concurrently is a genuine lost-update problem, and
///         it is one that a stale write should be told about rather than silently win.
///     </para>
/// </remarks>
public class Book : VersionedEntity
{
    /// <summary>
    ///     External identifier, generated on creation and used everywhere the book is addressed from
    ///     outside the database. The internal <see cref="Entity.Id" /> is never exposed to callers.
    /// </summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>Book title. Required, max 300 characters.</summary>
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    ///     ISBN, stored normalised (digits and a trailing <c>X</c>, upper case, no separators) and
    ///     unique across the catalogue.
    /// </summary>
    [MaxLength(13)]
    public string Isbn { get; set; } = string.Empty;

    /// <summary>Author name. Required, max 200 characters.</summary>
    [MaxLength(200)]
    public string Author { get; set; } = string.Empty;

    /// <summary>How many copies the library owns. Never negative.</summary>
    public int TotalCopies { get; set; }

    /// <summary>
    ///     How many copies are on the shelf right now. Always between zero and
    ///     <see cref="TotalCopies" />, enforced by check constraints as well as by the conditional
    ///     updates that maintain it.
    /// </summary>
    public int AvailableCopies { get; set; }

    /// <summary>
    ///     Whether the book may still be lent. Deactivation is how a book leaves the catalogue: the
    ///     row and its loan history stay, so nothing is erased silently.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Creation timestamp, UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last update timestamp, UTC.</summary>
    public DateTime UpdatedAt { get; set; }

    // Navigation properties

    /// <summary>Every loan ever made of this book, in any state.</summary>
    public ICollection<Loan> Loans { get; set; } = [];
}
