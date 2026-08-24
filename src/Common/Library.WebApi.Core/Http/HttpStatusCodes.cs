namespace Library.WebApi.Core.Http;

/// <summary>
///     The HTTP status codes this API returns, as plain integers. Named constants rather than
///     <see cref="System.Net.HttpStatusCode" /> casts so a message-to-status map reads as data.
/// </summary>
public static class HttpStatusCodes
{
    /// <summary>200 - the request succeeded.</summary>
    public const int Ok = 200;

    /// <summary>201 - a resource was created.</summary>
    public const int Created = 201;

    /// <summary>400 - the request was malformed or failed input validation.</summary>
    public const int BadRequest = 400;

    /// <summary>404 - the addressed resource does not exist.</summary>
    public const int NotFound = 404;

    /// <summary>409 - the request conflicts with the current state of the resource.</summary>
    public const int Conflict = 409;

    /// <summary>422 - the request is well-formed but a business rule refuses it.</summary>
    public const int UnprocessableEntity = 422;

    /// <summary>500 - an unhandled failure inside the API.</summary>
    public const int InternalServerError = 500;

    /// <summary>503 - a dependency the API needs to serve traffic is unavailable.</summary>
    public const int ServiceUnavailable = 503;
}
