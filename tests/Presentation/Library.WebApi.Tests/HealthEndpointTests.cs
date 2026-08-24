using System.Net;
using System.Text.Json;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Library.WebApi.Tests.Support;

namespace Library.WebApi.Tests;

// The two probe endpoints. The distinction they encode - liveness answers as long as the process is
// up, readiness answers for the dependencies needed to serve traffic - is what keeps a PostgreSQL
// outage from turning into a restart loop across every replica.
[Collection(nameof(FunctionalCollection))]
public class HealthEndpointTests(LibraryContainers containers) : LibraryApiTest(containers)
{
    [FunctionalFact]
    public async Task GivenARunningProcess_WhenProbingLiveness_ThenItAnswersHealthyWithNoChecks()
    {
        // When
        var response = await Gateway.GetAsync<JsonElement>("/health/live");

        // Then - and, crucially, it runs no checks: wiring the database in here would mean a database
        // outage causes Kubernetes to restart every replica in a loop, which cannot help
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", response.Body.GetProperty("status").GetString());
        Assert.Empty(response.Body.GetProperty("checks").EnumerateArray());
    }

    [FunctionalFact]
    public async Task GivenReachableDependencies_WhenProbingReadiness_ThenBothAreReported()
    {
        // When
        var response = await Gateway.GetAsync<JsonElement>("/health/ready");

        // Then
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var checks = response.Body.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("name").GetString())
            .ToList();

        Assert.Contains("postgresql", checks);
        Assert.Contains("redis", checks);
    }

    [FunctionalFact]
    public async Task GivenAHealthProbe_WhenItAnswers_ThenItCarriesNoExceptionDetail()
    {
        // When
        var response = await Gateway.GetAsync<JsonElement>("/health/ready");

        // Then - the body names which dependency is unhappy and nothing more; the detail belongs in
        // the log, where an operator reads it and an anonymous caller does not
        Assert.DoesNotContain("StackTrace", response.RawBody);
        Assert.DoesNotContain("Exception", response.RawBody);
    }
}
