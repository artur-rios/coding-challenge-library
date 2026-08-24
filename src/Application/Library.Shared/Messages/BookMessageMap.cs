using Library.WebApi.Core.Http;

namespace Library.Shared.Messages;

/// <summary>Maps each <see cref="BookMessages" /> value to its HTTP status code.</summary>
public static class BookMessageMap
{
    public static readonly IReadOnlyDictionary<string, int> StatusCodes =
        DataAccessMessageMap.CombinedWith(new Dictionary<string, int>
        {
            [BookMessages.BookCreatedSuccessfully] = HttpStatusCodes.Created,
            [BookMessages.BookRetrievedSuccessfully] = HttpStatusCodes.Ok,
            [BookMessages.BooksRetrievedSuccessfully] = HttpStatusCodes.Ok,
            [BookMessages.BookUpdatedSuccessfully] = HttpStatusCodes.Ok,
            // Deactivation and its already-inactive repeat answer the same 200: a caller that
            // deactivates twice has got what they asked for both times, and the message is what tells
            // the two paths apart.
            [BookMessages.BookDeactivatedSuccessfully] = HttpStatusCodes.Ok,
            [BookMessages.BookAlreadyInactive] = HttpStatusCodes.Ok,
            [BookMessages.AvailabilityRetrievedSuccessfully] = HttpStatusCodes.Ok,
            [BookMessages.HistoryRetrievedSuccessfully] = HttpStatusCodes.Ok,
            [BookMessages.BookNotFound] = HttpStatusCodes.NotFound,
            // 409 rather than 400: the request is well-formed and would have been accepted a moment
            // earlier - it collides with the state of the catalogue, which is what 409 says.
            [BookMessages.IsbnAlreadyRegistered] = HttpStatusCodes.Conflict,
            [BookMessages.BookHasActiveLoans] = HttpStatusCodes.Conflict,
            // 422 rather than 409: the request is well-formed and does not collide with another
            // record; a business rule refuses the value itself.
            [BookMessages.TotalCopiesBelowActiveLoans] = HttpStatusCodes.UnprocessableEntity,
            [BookMessages.NoFieldsToUpdate] = HttpStatusCodes.BadRequest,
            [BookMessages.TitleRequired] = HttpStatusCodes.BadRequest,
            [BookMessages.TitleTooLong] = HttpStatusCodes.BadRequest,
            [BookMessages.AuthorRequired] = HttpStatusCodes.BadRequest,
            [BookMessages.AuthorTooLong] = HttpStatusCodes.BadRequest,
            [BookMessages.IsbnRequired] = HttpStatusCodes.BadRequest,
            [BookMessages.IsbnInvalid] = HttpStatusCodes.BadRequest,
            [BookMessages.TotalCopiesNegative] = HttpStatusCodes.BadRequest,
            [BookMessages.TotalCopiesTooLarge] = HttpStatusCodes.BadRequest,
            [PaginationMessages.InvalidPageNumber] = HttpStatusCodes.BadRequest,
            [PaginationMessages.InvalidPageSize] = HttpStatusCodes.BadRequest,
            [PaginationMessages.FilterTooLong] = HttpStatusCodes.BadRequest
        });
}
