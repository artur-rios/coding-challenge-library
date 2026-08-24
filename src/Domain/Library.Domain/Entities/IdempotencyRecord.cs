using System.ComponentModel.DataAnnotations;
using Library.Data.Relational.Entities;

namespace Library.Domain.Entities;

/// <summary>
///     The response a previously-seen <c>Idempotency-Key</c> produced, so a repeat of the same
///     request answers the same thing instead of doing the work twice.
/// </summary>
/// <remarks>
///     <para>
///         The row is inserted inside the same transaction as the loan it describes. That is what
///         makes the guarantee hold rather than merely usually hold: the key and the loan become
///         visible together, so there is no window in which a retry can find the loan missing and
///         create a second one, nor one in which it finds a key with no response to return.
///     </para>
///     <para>
///         In PostgreSQL rather than Redis on purpose. Redis is a cache here and is allowed to lose
///         data; an idempotency guarantee that evaporates on eviction is not a guarantee, and the
///         thing being protected - a loan - lives in PostgreSQL anyway, so the two belong in one
///         transaction.
///     </para>
/// </remarks>
public class IdempotencyRecord : Entity
{
    /// <summary>The caller-supplied key, unique across the table.</summary>
    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    ///     The endpoint the key was used against, so the same key on a different operation is a
    ///     conflict rather than a silent replay of an unrelated response.
    /// </summary>
    [MaxLength(200)]
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    ///     A SHA-256 hash of the request payload the key first arrived with.
    /// </summary>
    /// <remarks>
    ///     Compared on a repeat: the same key with a different body is a caller bug, and answering it
    ///     with the first request's response would quietly do the wrong thing. It is a hash rather
    ///     than the body itself because the comparison only needs equality, and storing request
    ///     bodies verbatim is how a table like this becomes a copy of everything anybody ever sent.
    /// </remarks>
    [MaxLength(64)]
    public string RequestFingerprint { get; set; } = string.Empty;

    /// <summary>The HTTP status the first attempt answered with.</summary>
    public int StatusCode { get; set; }

    /// <summary>The response body the first attempt produced, as JSON, replayed verbatim.</summary>
    public string ResponseBody { get; set; } = "{}";

    /// <summary>When the key was first seen, UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     When the record may be swept, UTC. Retention is finite because a key is only ever retried
    ///     within minutes of the original; keeping every key forever would grow a table nothing reads.
    /// </summary>
    public DateTime ExpiresAt { get; set; }
}
