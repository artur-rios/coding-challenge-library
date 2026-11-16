using System.Diagnostics;
using System.Text.Json;
using Library.Testing.Attributes;
using Library.WebApi.Core.AspNetCore;
using Library.WebApi.Core.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace Library.Common.Tests.WebApiCore;

// Unit tests for the two middlewares every request passes through: the correlation identifier and
// the last-resort exception handler.
public class MiddlewareTests
{
    private const string Secret = "connection string Host=db;Password=hunter2";

    /// <summary>A response that records its OnStarting callbacks and can be told it has started.</summary>
    private sealed class RecordingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];

        public bool Started { get; set; }

        public override bool HasStarted => Started;

        public override void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public async Task StartAsync()
        {
            foreach (var (callback, state) in _onStarting)
            {
                await callback(state);
            }
        }
    }

    private static (DefaultHttpContext Context, RecordingResponseFeature Response) Context()
    {
        var response = new RecordingResponseFeature();
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(response);
        context.Response.Body = new MemoryStream();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/books";
        context.TraceIdentifier = "trace-1";

        return (context, response);
    }

    private static async Task<JsonElement> BodyOf(HttpContext context)
    {
        context.Response.Body.Position = 0;

        return (await JsonDocument.ParseAsync(context.Response.Body)).RootElement;
    }

    [UnitFact]
    public async Task GivenACorrelationIdHeader_WhenARequestPasses_ThenItIsKeptAndEchoed()
    {
        // Given
        var (context, response) = Context();
        context.Request.Headers[ResponseResolver.CorrelationIdHeader] = "caller-id";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);
        await response.StartAsync();

        // Then
        Assert.Equal("caller-id", context.Items[ResponseResolver.CorrelationIdItemKey]);
        Assert.Equal("caller-id", context.Response.Headers[ResponseResolver.CorrelationIdHeader].ToString());
    }

    [UnitTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(129)]
    public async Task GivenNoUsableCorrelationId_WhenARequestPasses_ThenANewOneIsMade(object? supplied)
    {
        // Given
        var (context, _) = Context();
        var header = supplied is int length ? new string('x', length) : (string?)supplied;

        if (header is not null)
        {
            context.Request.Headers[ResponseResolver.CorrelationIdHeader] = header;
        }

        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        var correlationId = Assert.IsType<string>(context.Items[ResponseResolver.CorrelationIdItemKey]);
        Assert.True(Guid.TryParse(correlationId, out _));
    }

    [UnitFact]
    public async Task GivenACorrelationIdOfExactlyTheLimit_WhenARequestPasses_ThenItIsKept()
    {
        // Given
        var (context, _) = Context();
        var supplied = new string('x', 128);
        context.Request.Headers[ResponseResolver.CorrelationIdHeader] = supplied;
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(supplied, context.Items[ResponseResolver.CorrelationIdItemKey]);
    }

    [UnitFact]
    public async Task GivenACurrentActivity_WhenARequestPasses_ThenItIsTaggedWithTheCorrelationId()
    {
        // Given
        var (context, _) = Context();
        context.Request.Headers[ResponseResolver.CorrelationIdHeader] = "caller-id";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, NullLogger<CorrelationIdMiddleware>.Instance);
        using var activity = new Activity("request").Start();

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal("caller-id", activity.GetTagItem(CorrelationIdMiddleware.ActivityTagName));
    }

    [UnitFact]
    public async Task GivenTheNextMiddlewareThrows_WhenHandled_ThenA500ProblemIsWrittenWithoutTheExceptionText()
    {
        // Given
        var (context, _) = Context();
        context.Items[ResponseResolver.CorrelationIdItemKey] = "correlation-1";
        var middleware = new ExceptionMiddleware(_ => throw new InvalidOperationException(Secret), NullLogger<ExceptionMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        var body = await BodyOf(context);
        Assert.Equal("Internal server error, please try again later.", body.GetProperty("title").GetString());
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.Equal("/books", body.GetProperty("instance").GetString());
        Assert.Equal("correlation-1", body.GetProperty("correlationId").GetString());
        Assert.Equal(["Internal server error, please try again later."], body.GetProperty("errors").EnumerateArray().Select(error => error.GetString()));
        Assert.DoesNotContain("hunter2", body.GetRawText());
    }

    [UnitFact]
    public async Task GivenACancellationTheCallerDidNotCause_WhenHandled_ThenItIsA500()
    {
        // Given
        var (context, _) = Context();
        var middleware = new ExceptionMiddleware(_ => throw new OperationCanceledException(), NullLogger<ExceptionMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
    }

    [UnitFact]
    public async Task GivenTheCallerWentAway_WhenTheRequestIsCanceled_ThenNothingIsWritten()
    {
        // Given
        var (context, _) = Context();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;
        var middleware = new ExceptionMiddleware(_ => throw new OperationCanceledException(), NullLogger<ExceptionMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [UnitFact]
    public async Task GivenTheCallerWentAway_WhenAnotherExceptionIsThrown_ThenNothingIsWritten()
    {
        // Given
        var (context, _) = Context();
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        context.RequestAborted = aborted.Token;
        var middleware = new ExceptionMiddleware(_ => throw new InvalidOperationException(Secret), NullLogger<ExceptionMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(0, context.Response.Body.Length);
    }

    [UnitFact]
    public async Task GivenTheResponseHasStarted_WhenAnExceptionIsThrown_ThenNothingMoreIsWritten()
    {
        // Given
        var (context, response) = Context();
        response.Started = true;
        var middleware = new ExceptionMiddleware(_ => throw new InvalidOperationException(Secret), NullLogger<ExceptionMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(0, context.Response.Body.Length);
    }

    [UnitFact]
    public async Task GivenNoException_WhenARequestPasses_ThenTheResponseIsLeftAlone()
    {
        // Given
        var (context, _) = Context();
        var middleware = new ExceptionMiddleware(
            httpContext =>
            {
                httpContext.Response.StatusCode = StatusCodes.Status201Created;

                return Task.CompletedTask;
            },
            NullLogger<ExceptionMiddleware>.Instance);

        // When
        await middleware.InvokeAsync(context);

        // Then
        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
    }

    [UnitFact]
    public void GivenTheFailureEnvelope_WhenBuilt_ThenItCarriesOnlyTheGenericError()
    {
        // When
        var envelope = ExceptionMiddleware.FailureEnvelope();

        // Then
        Assert.False(envelope.Success);
        Assert.Equal(["Internal server error, please try again later."], envelope.Errors);
    }
}
