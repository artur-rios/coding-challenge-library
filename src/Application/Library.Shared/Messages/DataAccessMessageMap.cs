using Library.Data.Relational.Repositories;
using Library.WebApi.Core.Http;

namespace Library.Shared.Messages;

/// <summary>
///     Maps the persistence layer's own classified failures to HTTP statuses, and merges them into
///     each feature's map.
/// </summary>
/// <remarks>
///     Without this, a unique-constraint violation that reached the controller as a
///     <c>RelationalErrors</c> message would fall through to the 400 default and be reported as if the
///     caller had sent something malformed. These are the statuses that failure class actually
///     deserves, stated once rather than repeated in every feature map.
/// </remarks>
public static class DataAccessMessageMap
{
    private static readonly Dictionary<string, int> StatusCodes = new()
    {
        [RelationalErrors.ConcurrencyMessage] = HttpStatusCodes.Conflict,
        [RelationalErrors.UniqueViolationMessage] = HttpStatusCodes.Conflict,
        [RelationalErrors.IntegrityViolationMessage] = HttpStatusCodes.Conflict,
        [RelationalErrors.TransientMessage] = HttpStatusCodes.ServiceUnavailable,
        [RelationalErrors.GenericMessage] = HttpStatusCodes.InternalServerError
    };

    /// <summary>Returns a map holding the data-access entries plus the caller's own.</summary>
    public static IReadOnlyDictionary<string, int> CombinedWith(IDictionary<string, int> featureStatusCodes)
    {
        var combined = new Dictionary<string, int>(StatusCodes);

        foreach (var (message, statusCode) in featureStatusCodes)
        {
            combined[message] = statusCode;
        }

        return combined;
    }
}
