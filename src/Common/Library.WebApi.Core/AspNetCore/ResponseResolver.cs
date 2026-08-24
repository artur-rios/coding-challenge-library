using Library.Output;
using Library.WebApi.Core.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Library.WebApi.Core.AspNetCore;

/// <summary>
///     Converts the output envelopes handlers return into ASP.NET Core results.
/// </summary>
/// <remarks>
///     <para>
///         The status is resolved in order: an explicit <c>statusCode</c>, then a lookup of the
///         envelope's first error (on failure) or first message (on success) in an optional
///         <c>statusMap</c>, then a default of 200 on success and 400 on failure. Keeping the map
///         beside the messages means the same refusal always leaves the API with the same status, no
///         matter which controller returned it.
///     </para>
///     <para>
///         A successful envelope is returned as it stands, so the caller gets the payload plus the
///         messages. A failed one is rewritten into RFC 9457 <see cref="ProblemDetails" />, because
///         that is the shape the challenge asks for and the one a generic client already knows how to
///         read. Nothing is lost in the rewrite: every message ends up in the <c>errors</c> extension,
///         and the correlation identifier is attached so a caller can quote it in a bug report.
///     </para>
/// </remarks>
public static class ResponseResolver
{
    /// <summary>Header carrying the correlation identifier, in and out.</summary>
    public const string CorrelationIdHeader = "X-Correlation-Id";

    /// <summary>Key under which the correlation identifier is published on <see cref="HttpContext.Items" />.</summary>
    public const string CorrelationIdItemKey = "CorrelationId";

    /// <summary>Wraps a <see cref="DataOutput{T}" />, rewriting a failure as problem details.</summary>
    public static ActionResult<DataOutput<T?>> Resolve<T>(
        DataOutput<T?> output,
        HttpContext httpContext,
        int? statusCode = null,
        IReadOnlyDictionary<string, int>? statusMap = null)
    {
        var resolved = ResolveStatusCode(output, statusCode, statusMap);

        return output.Success
            ? new ObjectResult(output) { StatusCode = resolved }
            : Problem(output, httpContext, resolved);
    }

    /// <summary>Wraps a <see cref="PaginatedOutput{T}" />, rewriting a failure as problem details.</summary>
    public static ActionResult<PaginatedOutput<T>> Resolve<T>(
        PaginatedOutput<T> output,
        HttpContext httpContext,
        int? statusCode = null,
        IReadOnlyDictionary<string, int>? statusMap = null)
    {
        var resolved = ResolveStatusCode(output, statusCode, statusMap);

        return output.Success
            ? new ObjectResult(output) { StatusCode = resolved }
            : Problem(output, httpContext, resolved);
    }

    /// <summary>
    ///     Builds the problem-details body for a failed envelope. Public so the exception middleware
    ///     answers in exactly the same shape a refused request does.
    /// </summary>
    public static ObjectResult Problem(ProcessOutput output, HttpContext httpContext, int statusCode)
    {
        var problem = new ProblemDetails
        {
            // The first error is the one the handler decided the status from, so it is also the one
            // that belongs in the title.
            Title = output.Errors.FirstOrDefault() ?? "Request failed.",
            Status = statusCode,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.io/{statusCode}"
        };

        // Every error, not only the title's: a validator commonly reports several at once, and a
        // caller fixing input needs all of them rather than one per round trip.
        problem.Extensions["errors"] = output.Errors;
        problem.Extensions["correlationId"] = CorrelationIdOf(httpContext);

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }

    /// <summary>Reads the correlation identifier the middleware published for this request.</summary>
    public static string CorrelationIdOf(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(CorrelationIdItemKey, out var value) && value is string correlationId
            ? correlationId
            : httpContext.TraceIdentifier;

    private static int ResolveStatusCode(
        ProcessOutput output, int? statusCode, IReadOnlyDictionary<string, int>? statusMap)
    {
        if (statusCode.HasValue)
        {
            return statusCode.Value;
        }

        if (statusMap is not null)
        {
            var key = output.Success ? output.Messages.FirstOrDefault() : output.Errors.FirstOrDefault();

            if (key is not null && statusMap.TryGetValue(key, out var mapped))
            {
                return mapped;
            }
        }

        return output.Success ? HttpStatusCodes.Ok : HttpStatusCodes.BadRequest;
    }
}
