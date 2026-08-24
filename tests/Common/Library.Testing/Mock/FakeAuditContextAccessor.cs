using Library.Shared.Auditing;

namespace Library.Testing.Mock;

/// <summary>A fixed actor and correlation identifier, for tests that assert on what the trail records.</summary>
public class FakeAuditContextAccessor(string actor = "test-actor", string? correlationId = null)
    : IAuditContextAccessor
{
    public string Actor { get; } = actor;

    public string CorrelationId { get; } = correlationId ?? Guid.NewGuid().ToString();
}
