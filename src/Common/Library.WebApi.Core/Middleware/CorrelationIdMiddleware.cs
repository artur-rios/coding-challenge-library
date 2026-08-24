using System.Diagnostics;
using Library.WebApi.Core.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Library.WebApi.Core.Middleware;

/// <summary>
///     Gives every request a correlation identifier, publishes it on <see cref="HttpContext.Items" />
///     and on the response header, and opens a logging scope so every log line the request produces
///     carries it.
/// </summary>
/// <remarks>
///     <para>
///         The identifier is taken from the incoming <c>X-Correlation-Id</c> header when the caller
///         supplies one, so a trail can be followed across the services in front of this API, and
///         generated otherwise. It is also stamped on the current <see cref="Activity" /> as a tag,
///         which is what puts it on the OpenTelemetry span alongside the trace identifier.
///     </para>
///     <para>
///         This is what the audit trail records as <c>correlation_id</c>: the requirement is that an
///         auditor can tie a stored business event back to the request that caused it, and a value
///         only the logs know would not do that.
///     </para>
/// </remarks>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>The tag the correlation identifier is stamped on the current activity under.</summary>
    public const string ActivityTagName = "correlation.id";

    private const int MaxAcceptedLength = 128;

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var correlationId = ResolveCorrelationId(httpContext);

        httpContext.Items[ResponseResolver.CorrelationIdItemKey] = correlationId;

        // Registered on OnStarting rather than written now: a later middleware may still replace the
        // response, and headers cannot be set once the body has begun.
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[ResponseResolver.CorrelationIdHeader] = correlationId;

            return Task.CompletedTask;
        });

        Activity.Current?.SetTag(ActivityTagName, correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(httpContext);
        }
    }

    private static string ResolveCorrelationId(HttpContext httpContext)
    {
        var supplied = httpContext.Request.Headers[ResponseResolver.CorrelationIdHeader].ToString();

        // A caller-supplied value is echoed, but only within a sane length: it is written to the
        // audit trail and to every log line, so an unbounded header would be a cheap way to bloat
        // both.
        return !string.IsNullOrWhiteSpace(supplied) && supplied.Length <= MaxAcceptedLength
            ? supplied
            : Guid.NewGuid().ToString();
    }
}
