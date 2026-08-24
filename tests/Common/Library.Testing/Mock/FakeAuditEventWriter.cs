using Library.Shared.Auditing;

namespace Library.Testing.Mock;

/// <summary>Records the audit entries a handler wrote, so a test can assert on the trail it leaves.</summary>
public class FakeAuditEventWriter : IAuditEventWriter
{
    /// <summary>One entry per <see cref="WriteAsync" /> call, in order.</summary>
    public List<RecordedEntry> Entries { get; } = [];

    /// <summary>When set, the next write throws - the failure mode the handlers deliberately do not swallow.</summary>
    public Exception? NextWriteFailure { get; set; }

    public Task WriteAsync(
        string entityType, Guid entityId, string action, object data, CancellationToken ct = default)
    {
        if (NextWriteFailure is { } failure)
        {
            NextWriteFailure = null;

            throw failure;
        }

        Entries.Add(new RecordedEntry(entityType, entityId, action, data));

        return Task.CompletedTask;
    }

    /// <summary>One recorded call.</summary>
    public record RecordedEntry(string EntityType, Guid EntityId, string Action, object Data);
}
