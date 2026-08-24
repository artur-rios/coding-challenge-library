using Library.WebApi.Core.Http;

namespace Library.Shared.Messages;

/// <summary>Maps each <see cref="AuditMessages" /> value to its HTTP status code.</summary>
public static class AuditMessageMap
{
    public static readonly IReadOnlyDictionary<string, int> StatusCodes =
        DataAccessMessageMap.CombinedWith(new Dictionary<string, int>
        {
            [AuditMessages.AuditEventsRetrievedSuccessfully] = HttpStatusCodes.Ok,
            [AuditMessages.InvalidDateRange] = HttpStatusCodes.BadRequest,
            [AuditMessages.InvalidEntityType] = HttpStatusCodes.BadRequest,
            [PaginationMessages.InvalidPageNumber] = HttpStatusCodes.BadRequest,
            [PaginationMessages.InvalidPageSize] = HttpStatusCodes.BadRequest,
            [PaginationMessages.FilterTooLong] = HttpStatusCodes.BadRequest
        });
}
