using System.Text.Json;
using Library.Output;
using Library.WebApi.Core.AspNetCore;
using Library.WebApi.Core.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Library.WebApi.Core.Middleware;

/// <summary>
///     Catches unhandled exceptions raised further down the pipeline and converts them into the same
///     problem-details response a refused request produces, logging the exception and quietly
///     ignoring client-initiated cancellations.
/// </summary>
/// <param name="next">The next middleware in the pipeline.</param>
/// <param name="logger">Used to log unhandled exceptions and cancellations.</param>
public class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await next(httpContext);
        }
        catch (OperationCanceledException exception) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The caller went away. Not an error of ours, and logging it as one would fill the log
            // with a failure every time somebody closes a tab.
            logger.LogDebug("Request was canceled by the client: {Message}", exception.Message);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(httpContext, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext httpContext, Exception exception)
    {
        var correlationId = ResponseResolver.CorrelationIdOf(httpContext);

        logger.LogError(
            exception, "Unhandled exception while processing {Method} {Path} ({CorrelationId})",
            httpContext.Request.Method, httpContext.Request.Path, correlationId);

        if (httpContext.RequestAborted.IsCancellationRequested || httpContext.Response.HasStarted)
        {
            logger.LogDebug(
                "Cannot write the error response: the request was aborted or the response has started.");

            return;
        }

        // Deliberately generic. The exception's own message can name a table, a column or a
        // connection string, and none of that belongs in a response - the log line above is where an
        // operator reads the detail, and the correlation identifier is what ties the two together.
        var problem = new ProblemDetails
        {
            Title = "Internal server error, please try again later.",
            Status = HttpStatusCodes.InternalServerError,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.io/{HttpStatusCodes.InternalServerError}"
        };

        problem.Extensions["errors"] = new List<string> { problem.Title };
        problem.Extensions["correlationId"] = correlationId;

        httpContext.Response.Clear();
        httpContext.Response.ContentType = "application/problem+json";
        httpContext.Response.StatusCode = HttpStatusCodes.InternalServerError;

        await httpContext.Response.WriteAsync(
            JsonSerializer.Serialize(problem, SerializerOptions), httpContext.RequestAborted);
    }

    /// <summary>
    ///     Builds the envelope an unhandled failure would have produced, for tests that assert the
    ///     shape without provoking a real exception.
    /// </summary>
    public static ProcessOutput FailureEnvelope() =>
        ProcessOutput.New.WithError("Internal server error, please try again later.");
}
