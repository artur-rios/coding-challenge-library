using Library.Command.Input;
using Library.Command.Output;
using Library.Mediator.Command;
using Library.Mediator.Query;
using Library.Output;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Messages;
using Library.WebApi.Core.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace Library.WebApi.Controllers;

/// <summary>Readers, and what they have borrowed.</summary>
[ApiController]
[Route("users")]
[Produces("application/json")]
public class UsersController(CommandMediator commandMediator, QueryMediator queryMediator) : ControllerBase
{
    /// <summary>Registers a reader.</summary>
    /// <response code="201">The reader was created.</response>
    /// <response code="400">The request failed validation.</response>
    /// <response code="409">A reader with this e-mail already exists.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DataOutput<CreateUserCommandOutput?>>> Create(
        [FromBody] CreateUserCommand command, CancellationToken ct)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<CreateUserCommand, CreateUserCommandOutput>(command, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: UserMessageMap.StatusCodes);
    }

    /// <summary>Lists every loan ever made to this reader, in any state, newest first.</summary>
    /// <remarks>
    ///     Returned and cancelled loans are included: this is the reader's history, not their current
    ///     shelf. Pass <c>status=Active</c> to narrow it to what they are holding right now.
    /// </remarks>
    /// <response code="200">A page of loans.</response>
    /// <response code="400">The pagination values were rejected.</response>
    /// <response code="404">No reader holds that identifier.</response>
    [HttpGet("{id:guid}/loans")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaginatedOutput<LoanOutput>>> Loans(
        Guid id, [FromQuery] GetUserLoansQuery query, CancellationToken ct)
    {
        query.UserId = id;

        var result = await queryMediator
            .ExecutePaginatedQueryAsync<GetUserLoansQuery, LoanOutput>(query, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: UserMessageMap.StatusCodes);
    }
}
