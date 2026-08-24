using Library.Mediator.Query;

namespace Library.Query.Input;

/// <summary>Reads the business audit trail, with the filters an investigation actually starts from.</summary>
public class ListAuditEventsQuery : BaseQuery
{
    /// <summary>Only entries about this kind of entity - <c>book</c>, <c>user</c> or <c>loan</c>.</summary>
    public string? EntityType { get; set; }

    /// <summary>Only entries about this entity.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>Only entries recording this action.</summary>
    public string? Action { get; set; }

    /// <summary>Only entries attributed to this actor.</summary>
    public string? Actor { get; set; }

    /// <summary>
    ///     Only entries produced by this request. The filter that turns the trail from a list into an
    ///     explanation: it collects everything one call to the API caused, across every entity it
    ///     touched.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>Only entries at or after this instant, UTC.</summary>
    public DateTime? From { get; set; }

    /// <summary>Only entries at or before this instant, UTC.</summary>
    public DateTime? To { get; set; }
}
