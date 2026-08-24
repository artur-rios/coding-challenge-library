using Library.WebApi.Core.Http;

namespace Library.Shared.Messages;

/// <summary>Maps each <see cref="UserMessages" /> value to its HTTP status code.</summary>
public static class UserMessageMap
{
    public static readonly IReadOnlyDictionary<string, int> StatusCodes =
        DataAccessMessageMap.CombinedWith(new Dictionary<string, int>
        {
            [UserMessages.UserCreatedSuccessfully] = HttpStatusCodes.Created,
            [UserMessages.UserLoansRetrievedSuccessfully] = HttpStatusCodes.Ok,
            [UserMessages.UserNotFound] = HttpStatusCodes.NotFound,
            [UserMessages.EmailAlreadyRegistered] = HttpStatusCodes.Conflict,
            [UserMessages.NameRequired] = HttpStatusCodes.BadRequest,
            [UserMessages.NameTooLong] = HttpStatusCodes.BadRequest,
            [UserMessages.EmailRequired] = HttpStatusCodes.BadRequest,
            [UserMessages.EmailInvalid] = HttpStatusCodes.BadRequest,
            [UserMessages.EmailTooLong] = HttpStatusCodes.BadRequest,
            [PaginationMessages.InvalidPageNumber] = HttpStatusCodes.BadRequest,
            [PaginationMessages.InvalidPageSize] = HttpStatusCodes.BadRequest
        });
}
