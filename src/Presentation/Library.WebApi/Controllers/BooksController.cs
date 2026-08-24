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

/// <summary>The catalogue: books, their availability, and their loan history.</summary>
[ApiController]
[Route("books")]
[Produces("application/json")]
public class BooksController(CommandMediator commandMediator, QueryMediator queryMediator) : ControllerBase
{
    /// <summary>Adds a title to the catalogue.</summary>
    /// <remarks>
    ///     The ISBN is normalised before it is stored, so <c>978-0-306-40615-7</c> and
    ///     <c>9780306406157</c> are the same book and the second one is refused with 409.
    /// </remarks>
    /// <response code="201">The book was created.</response>
    /// <response code="400">The request failed validation.</response>
    /// <response code="409">A book with this ISBN already exists.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DataOutput<BookCommandOutput?>>> Create(
        [FromBody] CreateBookCommand command, CancellationToken ct)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<CreateBookCommand, BookCommandOutput>(command, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: BookMessageMap.StatusCodes);
    }

    /// <summary>Lists the catalogue, filtered and paginated.</summary>
    /// <remarks>Served from Redis when a matching page is cached; every write to the catalogue drops it.</remarks>
    /// <response code="200">A page of books.</response>
    /// <response code="400">The pagination or filter values were rejected.</response>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PaginatedOutput<BookOutput>>> List(
        [FromQuery] ListBooksQuery query, CancellationToken ct)
    {
        var result = await queryMediator
            .ExecutePaginatedQueryAsync<ListBooksQuery, BookOutput>(query, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: BookMessageMap.StatusCodes);
    }

    /// <summary>Reads one book, deactivated ones included.</summary>
    /// <response code="200">The book.</response>
    /// <response code="404">No book holds that identifier.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DataOutput<BookOutput?>>> GetById(Guid id, CancellationToken ct)
    {
        var result = await queryMediator
            .ExecuteQueryAsync<GetBookByIdQuery, BookOutput>(new GetBookByIdQuery { Id = id }, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: BookMessageMap.StatusCodes);
    }

    /// <summary>Changes a book's details. Omitted fields are left alone.</summary>
    /// <remarks>
    ///     Changing the number of copies moves the available count by the same delta, atomically.
    ///     Shrinking the total below the number of copies currently on loan is refused with 422.
    /// </remarks>
    /// <response code="200">The book as it now stands.</response>
    /// <response code="400">The request failed validation, or supplied no fields.</response>
    /// <response code="404">No book holds that identifier.</response>
    /// <response code="409">Another book already carries the submitted ISBN, or the book changed underneath.</response>
    /// <response code="422">The new total is below the number of copies on loan.</response>
    [HttpPatch("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DataOutput<BookCommandOutput?>>> Update(
        Guid id, [FromBody] UpdateBookCommand command, CancellationToken ct)
    {
        command.Id = id;

        var result = await commandMediator
            .ExecuteCommandAsync<UpdateBookCommand, BookCommandOutput>(command, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: BookMessageMap.StatusCodes);
    }

    /// <summary>Withdraws a book from lending.</summary>
    /// <remarks>
    ///     This endpoint deactivates; it never deletes. A book with loans has a history, and the
    ///     history is not erased - the response reports how many loans were preserved. Repeating the
    ///     call is a 200 with <c>alreadyInactive</c> set.
    /// </remarks>
    /// <response code="200">The book is inactive.</response>
    /// <response code="404">No book holds that identifier.</response>
    /// <response code="409">Copies are still out on loan.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DataOutput<DeactivateBookCommandOutput?>>> Deactivate(
        Guid id, CancellationToken ct)
    {
        var result = await commandMediator
            .ExecuteCommandAsync<DeactivateBookCommand, DeactivateBookCommandOutput>(
                new DeactivateBookCommand { Id = id }, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: BookMessageMap.StatusCodes);
    }

    /// <summary>Reports how many copies of a book are on the shelf.</summary>
    /// <remarks>
    ///     Served from Redis, and advisory: <c>calculatedAt</c> says when the figure was taken. Whether
    ///     a loan is actually granted is decided against PostgreSQL inside <c>POST /loans</c>, never
    ///     from this value.
    /// </remarks>
    /// <response code="200">The availability figures.</response>
    /// <response code="404">No book holds that identifier.</response>
    [HttpGet("{id:guid}/availability")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DataOutput<BookAvailabilityOutput?>>> Availability(
        Guid id, CancellationToken ct)
    {
        var result = await queryMediator
            .ExecuteQueryAsync<GetBookAvailabilityQuery, BookAvailabilityOutput>(
                new GetBookAvailabilityQuery { BookId = id }, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: BookMessageMap.StatusCodes);
    }

    /// <summary>Lists every loan ever made of this book, in any state, newest first.</summary>
    /// <response code="200">A page of loans.</response>
    /// <response code="400">The pagination values were rejected.</response>
    /// <response code="404">No book holds that identifier.</response>
    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaginatedOutput<LoanOutput>>> History(
        Guid id, [FromQuery] GetBookHistoryQuery query, CancellationToken ct)
    {
        query.BookId = id;

        var result = await queryMediator
            .ExecutePaginatedQueryAsync<GetBookHistoryQuery, LoanOutput>(query, ct);

        return ResponseResolver.Resolve(result, HttpContext, statusMap: BookMessageMap.StatusCodes);
    }
}
