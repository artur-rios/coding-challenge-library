using Library.WebApi.Core.Http;

namespace Library.Shared.Messages;

/// <summary>Maps each <see cref="LoanMessages" /> value to its HTTP status code.</summary>
public static class LoanMessageMap
{
    public static readonly IReadOnlyDictionary<string, int> StatusCodes =
        DataAccessMessageMap.CombinedWith(new Dictionary<string, int>
        {
            [LoanMessages.LoanCreatedSuccessfully] = HttpStatusCodes.Created,
            [LoanMessages.LoanReturnedSuccessfully] = HttpStatusCodes.Ok,
            [LoanMessages.LoanCancelledSuccessfully] = HttpStatusCodes.Ok,
            [LoanMessages.LoanNotFound] = HttpStatusCodes.NotFound,
            [BookMessages.BookNotFound] = HttpStatusCodes.NotFound,
            [UserMessages.UserNotFound] = HttpStatusCodes.NotFound,
            // 409, and this is the status the concurrency requirement turns on: the loser of a race
            // for the last copy is told, clearly and specifically, that the state of the book refused
            // the request - not that their request was malformed (400) and not that the API broke
            // (500).
            [LoanMessages.NoCopiesAvailable] = HttpStatusCodes.Conflict,
            [LoanMessages.BookNotAvailableForLoan] = HttpStatusCodes.Conflict,
            [LoanMessages.UserAlreadyHasCopy] = HttpStatusCodes.Conflict,
            [LoanMessages.LoanAlreadyReturned] = HttpStatusCodes.Conflict,
            [LoanMessages.LoanAlreadyCancelled] = HttpStatusCodes.Conflict,
            [LoanMessages.IdempotencyKeyReused] = HttpStatusCodes.Conflict,
            [LoanMessages.IdempotentRequestInFlight] = HttpStatusCodes.Conflict,
            [LoanMessages.IdempotencyKeyRequired] = HttpStatusCodes.BadRequest,
            [LoanMessages.IdempotencyKeyTooLong] = HttpStatusCodes.BadRequest,
            [LoanMessages.LoanPeriodOutOfRange] = HttpStatusCodes.BadRequest,
            [LoanMessages.BookIdRequired] = HttpStatusCodes.BadRequest,
            [LoanMessages.UserIdRequired] = HttpStatusCodes.BadRequest,
            // 500: the caller did nothing wrong and retrying will not help until somebody looks.
            [LoanMessages.AvailabilityInconsistent] = HttpStatusCodes.InternalServerError,
            [PaginationMessages.InvalidPageNumber] = HttpStatusCodes.BadRequest,
            [PaginationMessages.InvalidPageSize] = HttpStatusCodes.BadRequest
        });
}
