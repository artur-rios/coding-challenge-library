using Library.Output;
using Library.Testing.Attributes;
using Library.WebApi.Core.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Library.Common.Tests.WebApiCore;

// Unit tests for ResponseResolver: which status an envelope is answered with, and what a failure's
// problem details carry.
public class ResponseResolverTests
{
    private static readonly IReadOnlyDictionary<string, int> StatusMap = new Dictionary<string, int>
    {
        ["Created"] = StatusCodes.Status201Created,
        ["Not found"] = StatusCodes.Status404NotFound
    };

    private static HttpContext Context(string? correlationId = null)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        context.Request.Path = "/books/1";

        if (correlationId is not null)
        {
            context.Items[ResponseResolver.CorrelationIdItemKey] = correlationId;
        }

        return context;
    }

    private static ObjectResult Resolve(DataOutput<string?> output, int? statusCode = null, IReadOnlyDictionary<string, int>? map = null) =>
        Assert.IsType<ObjectResult>(ResponseResolver.Resolve(output, Context(), statusCode, map).Result);

    [UnitFact]
    public void GivenASuccess_WhenResolvedWithNothingElse_ThenItIsOkWithTheEnvelope()
    {
        // Given
        var output = DataOutput<string?>.New.WithData("data");

        // When
        var result = Resolve(output);

        // Then
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Same(output, result.Value);
    }

    [UnitFact]
    public void GivenAFailure_WhenResolvedWithNothingElse_ThenItIsABadRequestProblem()
    {
        // When
        var result = Resolve(DataOutput<string?>.New.WithError("Invalid"));

        // Then
        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.IsType<ProblemDetails>(result.Value);
    }

    [UnitFact]
    public void GivenAnExplicitStatus_WhenResolved_ThenItWinsOverTheMap()
    {
        // When
        var result = Resolve(DataOutput<string?>.New.WithMessage("Created"), StatusCodes.Status202Accepted, StatusMap);

        // Then
        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
    }

    [UnitFact]
    public void GivenASuccessWhoseFirstMessageIsMapped_WhenResolved_ThenTheMappedStatusIsUsed()
    {
        // When
        var result = Resolve(DataOutput<string?>.New.WithMessage("Created").WithMessage("Not found"), map: StatusMap);

        // Then
        Assert.Equal(StatusCodes.Status201Created, result.StatusCode);
    }

    [UnitFact]
    public void GivenAFailureWhoseFirstErrorIsMapped_WhenResolved_ThenTheMappedStatusIsUsed()
    {
        // When
        var result = Resolve(DataOutput<string?>.New.WithError("Not found").WithError("Created"), map: StatusMap);

        // Then
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
    }

    [UnitTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void GivenAnOutcomeTheMapDoesNotName_WhenResolved_ThenTheDefaultStatusIsUsed(bool success)
    {
        // Given
        var output = success ? DataOutput<string?>.New.WithMessage("Other") : DataOutput<string?>.New.WithError("Other");

        // When
        var result = Resolve(output, map: StatusMap);

        // Then
        Assert.Equal(success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest, result.StatusCode);
    }

    [UnitFact]
    public void GivenASuccessWithNoMessage_WhenResolvedWithAMap_ThenTheDefaultStatusIsUsed()
    {
        // When
        var result = Resolve(DataOutput<string?>.New, map: StatusMap);

        // Then
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [UnitFact]
    public void GivenAPaginatedFailure_WhenResolved_ThenItIsAProblemWithTheMappedStatus()
    {
        // Given
        var output = PaginatedOutput<int>.New.WithError("Not found");

        // When
        var result = Assert.IsType<ObjectResult>(ResponseResolver.Resolve(output, Context(), statusMap: StatusMap).Result);

        // Then
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.IsType<ProblemDetails>(result.Value);
    }

    [UnitFact]
    public void GivenAPaginatedSuccess_WhenResolved_ThenItIsOkWithTheEnvelope()
    {
        // Given
        var output = PaginatedOutput<int>.New.WithData([1]);

        // When
        var result = Assert.IsType<ObjectResult>(ResponseResolver.Resolve(output, Context()).Result);

        // Then
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
        Assert.Same(output, result.Value);
    }

    [UnitFact]
    public void GivenSeveralErrors_WhenTurnedIntoAProblem_ThenTheFirstIsTheTitleAndAllAreListed()
    {
        // Given
        var output = ProcessOutput.New.WithError("First").WithError("Second");

        // When
        var result = ResponseResolver.Problem(output, Context("correlation-1"), StatusCodes.Status409Conflict);

        // Then
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(StatusCodes.Status409Conflict, result.StatusCode);
        Assert.Equal(["application/problem+json"], result.ContentTypes);
        Assert.Equal("First", problem.Title);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("/books/1", problem.Instance);
        Assert.Equal("https://httpstatuses.io/409", problem.Type);
        Assert.Equal(["First", "Second"], Assert.IsType<List<string>>(problem.Extensions["errors"]));
        Assert.Equal("correlation-1", problem.Extensions["correlationId"]);
    }

    [UnitFact]
    public void GivenNoError_WhenTurnedIntoAProblem_ThenTheTitleIsGeneric()
    {
        // When
        var result = ResponseResolver.Problem(ProcessOutput.New, Context(), StatusCodes.Status400BadRequest);

        // Then
        Assert.Equal("Request failed.", Assert.IsType<ProblemDetails>(result.Value).Title);
    }

    [UnitFact]
    public void GivenACorrelationIdInTheItems_WhenRead_ThenItIsReturned()
    {
        // When
        var correlationId = ResponseResolver.CorrelationIdOf(Context("correlation-1"));

        // Then
        Assert.Equal("correlation-1", correlationId);
    }

    [UnitFact]
    public void GivenNoCorrelationIdInTheItems_WhenRead_ThenTheTraceIdentifierIsReturned()
    {
        // Given
        var context = Context();
        context.Items[ResponseResolver.CorrelationIdItemKey] = 42;

        // When
        var correlationId = ResponseResolver.CorrelationIdOf(context);

        // Then
        Assert.Equal("trace-1", correlationId);
    }
}
