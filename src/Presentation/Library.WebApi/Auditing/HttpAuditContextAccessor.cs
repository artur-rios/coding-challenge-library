using Library.Shared.Auditing;
using Library.WebApi.Core.AspNetCore;

namespace Library.WebApi.Auditing;

/// <summary>
///     Reads the acting identity and the correlation identifier off the current request.
/// </summary>
/// <remarks>
///     <para>
///         The actor comes from an <c>X-Actor</c> header. This API has no authentication of its own -
///         the challenge scopes it out - so the header is taken at face value, and the value
///         <c>anonymous</c> is recorded when it is absent. That is a real limitation and it is stated
///         in the README rather than hidden here: the trail is honest about what it knows, which is
///         "the caller claimed to be X", not "X did this".
///     </para>
///     <para>
///         In a deployment with authentication in front of it, this is the one class that changes: the
///         actor would come from the validated token's subject claim and nothing else in the codebase
///         would notice.
///     </para>
/// </remarks>
public class HttpAuditContextAccessor(IHttpContextAccessor httpContextAccessor) : IAuditContextAccessor
{
    /// <summary>The header the acting identity is read from.</summary>
    public const string ActorHeader = "X-Actor";

    /// <summary>What is recorded when the caller names nobody.</summary>
    public const string AnonymousActor = "anonymous";

    /// <inheritdoc />
    public string Actor
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;

            if (httpContext is null)
            {
                // No request in flight - a background sweep, or a test calling a handler directly.
                return AnonymousActor;
            }

            var actor = httpContext.Request.Headers[ActorHeader].ToString();

            return string.IsNullOrWhiteSpace(actor) ? AnonymousActor : actor.Trim();
        }
    }

    /// <inheritdoc />
    public string CorrelationId
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;

            return httpContext is null
                ? Guid.NewGuid().ToString()
                : ResponseResolver.CorrelationIdOf(httpContext);
        }
    }
}
