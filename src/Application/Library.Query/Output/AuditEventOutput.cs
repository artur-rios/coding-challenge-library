using System.Text.Json;
using System.Text.Json.Nodes;
using Library.Mediator.Query;

namespace Library.Query.Output;

/// <summary>One audit trail entry as <c>GET /audit-events</c> reports it.</summary>
public class AuditEventOutput : QueryOutput
{
    /// <summary>The entry's public identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>The affected entity kind.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The affected entity's public identifier.</summary>
    public Guid EntityId { get; set; }

    /// <summary>What happened.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Who caused it.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>When it happened, UTC.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>The correlation identifier of the request that caused it.</summary>
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    ///     The stored detail, as JSON rather than as a string containing JSON.
    /// </summary>
    /// <remarks>
    ///     Parsed on the way out so the response nests a real object. Returning the raw column would
    ///     hand every client an escaped string to parse a second time, which is the sort of small
    ///     indignity an audit API should not inflict on the person reading it.
    /// </remarks>
    public JsonNode? Data { get; set; }

    /// <summary>Builds the output from the stored column, tolerating a value that will not parse.</summary>
    public static JsonNode? ParseData(string data)
    {
        try
        {
            return JsonNode.Parse(data);
        }
        catch (JsonException)
        {
            // A row written before the column was jsonb, or by something other than this API. The
            // trail is append-only and read-only, so the entry is reported as it stands rather than
            // dropped from the answer.
            return JsonValue.Create(data);
        }
    }
}
