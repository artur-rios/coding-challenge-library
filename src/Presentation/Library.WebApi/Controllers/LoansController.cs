using Library.Command.Input;
using Library.Command.Output;
using Library.Mediator.Command;
using Library.Output;
using Library.Shared.Messages;
using Library.WebApi.Core.AspNetCore;
using Microsoft.AspNetCore.Mvc;

namespace Library.WebApi.Controllers;

/// <summary>Lending: taking a copy out, bringing it back, and calling a loan off.</summary>
[ApiController]
[Route("loans")]
[Produces("application/json")]
public class LoansController(CommandMediator commandMediator) : ControllerBase
{
    /// <summary>The header carrying the idempotency key. Required by <see cref="Create" />.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Lends one copy of a book to a reader.</summary>
    /// <remarks>
    ///     <para>
    ///         Requires an <c>Idempotency-Key</c> header. Repeating the same key with the same body
    ///         returns the response the first call produced, with <c>replayed: true</c>, and creates
    ///         no second loan; repeating it with a different body is refused with 409.
    ///     </para>
    ///     <para>
    ///         Two concurrent requests for the last copy produce exactly one loan; the other is
    ///         refused with 409 and the message that no copies are available.
    ///     </para>
    /// </remarks>
    /// <param name="idempotencyKey">The <c>Idempotency-Key</c> header value.</param>
    /// <param name="command">The book, the reader and how long the loan runs.</param>
    /// <param name="ct">Cancellation token, propagated all the way to PostgreSQL.</param>
    /// <response code="201">The loan was created, or a previous identical request is being replayed.</response>
    /// <response code="400">The request failed validation, or carried no idempotency key.</response>
    /// <response code="404">No such book or reader.</response>
    /// <response code="409">No copies available, the book is withdrawn, the reader already holds a copy, or the key was reused.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DataOutput<LoanCommandOutput?>>> Create(
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] CreateLoanCommand command,
        CancellationToken ct)
    {
        // Applied from the header rather than bound from the body: it describes the request, not the
        // loan, and a caller must not be able to put it in the payload where it would become part of
        // its own fingerprint.
        command.IdempotencyKey = idempotencyKey?.Trim() ?? string.Empty;

        var result = await commandMediator
            .ExecuteCommandAsync<CreateLoanCommand, LoanCommandOutput>(command, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: LoanMessageMap.StatusCodes);
    }

    /// <summary>Records that a lent copy came back.</summary>
    /// <remarks>The loan is kept and moves to <c>Returned</c>; the copy goes back on the shelf.</remarks>
    /// <response code="200">The loan was returned.</response>
    /// <response code="404">No loan holds that identifier.</response>
    /// <response code="409">The loan was already returned or cancelled.</response>
    [HttpPost("{id:guid}/return")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DataOutput<LoanCommandOutput?>>> Return(Guid id, CancellationToken ct)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<ReturnLoanCommand, LoanCommandOutput>(
                new ReturnLoanCommand { Id = id }, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: LoanMessageMap.StatusCodes);
    }

    /// <summary>Calls a loan off.</summary>
    /// <remarks>
    ///     The row is kept and moves to <c>Cancelled</c> - a distinct state from <c>Returned</c>,
    ///     because a cancelled loan is not one the reader ever fulfilled, and the history has to be
    ///     able to say so.
    /// </remarks>
    /// <response code="200">The loan was cancelled.</response>
    /// <response code="404">No loan holds that identifier.</response>
    /// <response code="409">The loan was already returned or cancelled.</response>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DataOutput<LoanCommandOutput?>>> Cancel(
        Guid id, [FromBody] CancelLoanCommand? command, CancellationToken ct)
    {
        // The body is optional - a cancellation with no reason is ordinary - so a missing one is an
        // empty command rather than a 400.
        var cancellation = command ?? new CancelLoanCommand();
        cancellation.Id = id;

        var result = await commandMediator
            .ExecuteCommandAsync<CancelLoanCommand, LoanCommandOutput>(cancellation, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: LoanMessageMap.StatusCodes);
    }
}
