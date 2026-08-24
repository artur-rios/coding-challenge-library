using Library.Mediator.Query;
using Library.Output;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Messages;
using Library.WebApi.Core.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace Library.WebApi.Controllers;

/// <summary>The business audit trail.</summary>
[ApiController]
[Route("audit-events")]
[Produces("application/json")]
public class AuditEventsController(QueryMediator queryMediator) : ControllerBase
{
    /// <summary>Reads the audit trail, newest first.</summary>
    /// <remarks>
    ///     <para>
    ///         Read-only by design: there is no endpoint that writes, edits or deletes an entry, and
    ///         the entries themselves are written inside the transaction of the change they describe.
    ///     </para>
    ///     <para>
    ///         Filter by <c>correlationId</c> to see everything one request caused across every entity
    ///         it touched - the value is returned on every response in the <c>X-Correlation-Id</c>
    ///         header, so a caller can go straight from a response to its trail.
    ///     </para>
    /// </remarks>
    /// <response code="200">A page of audit entries.</response>
    /// <response code="400">The pagination, range or filter values were rejected.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedOutput<AuditEventOutput>>> List(
        [FromQuery] ListAuditEventsQuery query, CancellationToken ct)
    {
        var result = await queryMediator
            .ExecutePaginatedQueryAsync<ListAuditEventsQuery, AuditEventOutput>(query, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: AuditMessageMap.StatusCodes);
    }
}
