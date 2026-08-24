using System.ComponentModel.DataAnnotations;
using Library.Data.Relational.Entities;

namespace Library.Domain.Entities;

/// <summary>
///     One entry in the business audit trail: what changed, to which entity, at whose hand, when, and
///     under which request.
/// </summary>
/// <remarks>
///     <para>
///         Append-only. Nothing updates or deletes a row here, and the entity carries no navigation
///         property to the thing it describes - <see cref="EntityId" /> is a bare <c>PublicId</c>
///         rather than a foreign key, so an entry outlives whatever it refers to.
///     </para>
///     <para>
///         This is not the technical log. The log records what the process did; this records what the
///         library did, and it is the artefact somebody reconstructs a disputed loan from months
///         later. That is why it lives in PostgreSQL beside the data it describes, written in the same
///         transaction as the change itself, rather than in whatever the log sink happens to retain.
///     </para>
/// </remarks>
public class AuditEvent : Entity
{
    /// <summary>External identifier of this entry.</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>The kind of entity the change affected - see <c>AuditEntityTypes</c>.</summary>
    [MaxLength(50)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The affected entity's public identifier.</summary>
    public Guid EntityId { get; set; }

    /// <summary>What happened - see <c>AuditActions</c>.</summary>
    [MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    ///     Who caused it, as supplied by the caller in the <c>X-Actor</c> header, or
    ///     <c>"anonymous"</c> when it named nobody.
    /// </summary>
    /// <remarks>
    ///     A free-text identity because this API has no authentication of its own - the challenge
    ///     scopes it out. It is deliberately not a foreign key to <see cref="User" />: the actor is
    ///     usually a librarian operating the system, not the reader the loan is for, and conflating
    ///     the two would make the trail claim readers did things they did not do.
    /// </remarks>
    [MaxLength(200)]
    public string Actor { get; set; } = string.Empty;

    /// <summary>When it happened, always UTC.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>
    ///     The correlation identifier of the request that caused the change, so a trail entry, the
    ///     log lines and the trace all point at one another.
    /// </summary>
    [MaxLength(128)]
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    ///     A JSON object holding enough of the change to understand it without the reader having to
    ///     find the row - for a loan, the book, the reader and the due date; for a book update, the
    ///     before and after of each field that moved.
    /// </summary>
    /// <remarks>
    ///     Stored as <c>jsonb</c>, so an operator can query inside it rather than only read it back.
    ///     The writers put business facts here and nothing else: the trail is retained far longer than
    ///     an ordinary table, which makes it the worst place to accumulate unnecessary detail.
    /// </remarks>
    public string Data { get; set; } = "{}";
}
