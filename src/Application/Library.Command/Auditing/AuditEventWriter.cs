using System.Text.Json;
using Library.Data.Relational.Interfaces;
using Library.Domain.Entities;
using Library.Shared.Auditing;

namespace Library.Command.Auditing;

/// <summary>
///     Writes business audit entries through the same repository - and therefore the same
///     <c>DbContext</c> and the same transaction - as the change they describe.
/// </summary>
/// <remarks>
///     <para>
///         Heimdall, which this codebase follows, records its trail from a decorator around each
///         command handler. That is the right shape when the trail is a record of attempts, including
///         refused ones, and when losing an entry must not fail the request. It is the wrong shape
///         here: this trail is a business record of what happened to the library's stock, and an
///         entry written outside the transaction can be committed while the loan is rolled back, or
///         lost while the loan stands. Both leave the trail lying.
///     </para>
///     <para>
///         So the handlers call this writer explicitly, inside their transaction, and a failure to
///         write the entry fails the operation. That is the intended trade: a loan that cannot be
///         audited does not happen.
///     </para>
/// </remarks>
public class AuditEventWriter(
    IAsyncRepository<AuditEvent> repository,
    IAuditContextAccessor auditContext) : IAuditEventWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Longest actor recorded; matches the column.</summary>
    private const int MaxActorLength = 200;

    /// <summary>Longest correlation identifier recorded; matches the column.</summary>
    private const int MaxCorrelationIdLength = 128;

    public async Task WriteAsync(
        string entityType, Guid entityId, string action, object data, CancellationToken ct = default)
    {
        var entry = new AuditEvent
        {
            PublicId = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Actor = Truncate(auditContext.Actor, MaxActorLength),
            // Always UTC, never local: the trail is read by people in other places and compared
            // against other systems, and a timestamp whose zone depends on which container wrote it
            // cannot be ordered against anything.
            OccurredAt = DateTime.UtcNow,
            CorrelationId = Truncate(auditContext.CorrelationId, MaxCorrelationIdLength),
            Data = JsonSerializer.Serialize(data, data.GetType(), SerializerOptions)
        };

        var creation = await repository.CreateAsync(entry, ct);

        if (!creation.Success)
        {
            // Thrown rather than returned: the caller is inside a transaction, and the only correct
            // response to "the trail could not be written" is for the change to not happen either.
            // The transaction wrapper turns this back into an error envelope for the caller.
            throw new InvalidOperationException(
                $"Failed to write the audit entry for {action}: {string.Join("; ", creation.Errors)}");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;
}
