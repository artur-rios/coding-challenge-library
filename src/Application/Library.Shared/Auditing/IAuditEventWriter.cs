namespace Library.Shared.Auditing;

/// <summary>Writes one entry to the business audit trail.</summary>
public interface IAuditEventWriter
{
    /// <summary>
    ///     Records that <paramref name="action" /> happened to the entity identified by
    ///     <paramref name="entityType" /> and <paramref name="entityId" />, attributing it to the
    ///     current actor and correlation identifier.
    /// </summary>
    /// <param name="entityType">The affected entity kind - see <c>AuditEntityTypes</c>.</param>
    /// <param name="entityId">The affected entity's public identifier.</param>
    /// <param name="action">What happened - see <c>AuditActions</c>.</param>
    /// <param name="data">
    ///     Business facts about the change, serialized to JSON. Enough to understand the change
    ///     without reading the row it describes, and nothing beyond that.
    /// </param>
    /// <param name="ct">A token observed for cancellation.</param>
    /// <remarks>
    ///     The entry is added to the current unit of work and saved with it. A caller inside a
    ///     transaction therefore gets the change and its trail entry committed together, or neither -
    ///     which is the only arrangement in which the trail cannot disagree with the data.
    /// </remarks>
    Task WriteAsync(
        string entityType, Guid entityId, string action, object data, CancellationToken ct = default);
}
