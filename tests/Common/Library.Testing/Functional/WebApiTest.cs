using Library.Testing.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Testing.Functional;

/// <summary>
///     Base class for end-to-end tests. Spins up the real host in memory with
///     <see cref="WebApplicationFactory{TEntryPoint}" /> and exposes an <see cref="HttpGateway" /> over
///     it.
/// </summary>
/// <typeparam name="TEntryPoint">The API's entry point type - its <c>Program</c>.</typeparam>
/// <remarks>
///     The whole host: the same middleware order, the same model binding, the same problem-details
///     rewriting, the same dependency graph. Tests that construct a controller directly prove that a
///     method returns an object; these prove that a caller sending JSON over HTTP gets the status and
///     body the contract promises, which is the thing the contract actually is.
/// </remarks>
public class WebApiTest<TEntryPoint> : IDisposable where TEntryPoint : class
{
    private readonly WebApplicationFactory<TEntryPoint> _factory;

    /// <summary>The gateway used to issue requests against the in-memory host.</summary>
    protected readonly HttpGateway Gateway;

    /// <summary>Starts the host in the given environment.</summary>
    /// <param name="environment">The environment name, e.g. <c>Development</c>.</param>
    protected WebApiTest(string environment = "Development")
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment);

        // Told to the host directly as well as through the variable, so its environment does not
        // depend on whether anything else in the process has since changed it.
        _factory = new WebApplicationFactory<TEntryPoint>()
            .WithWebHostBuilder(builder => builder.UseEnvironment(environment));

        Gateway = new HttpGateway(_factory.CreateClient());
    }

    /// <summary>Creates an additional client over the same host, for tests that issue concurrent requests.</summary>
    /// <remarks>
    ///     The concurrency test needs several requests genuinely in flight at once. They can share one
    ///     <see cref="HttpClient" /> - it is thread-safe - but separate clients keep each request's
    ///     default headers (the idempotency key, above all) independent.
    /// </remarks>
    protected HttpGateway NewGateway() => new(_factory.CreateClient());

    /// <summary>Resolves a service from the running host's container.</summary>
    protected TService GetService<TService>() where TService : notnull =>
        _factory.Services.GetRequiredService<TService>();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the in-memory host. Override to dispose additional resources.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _factory.Dispose();
        }
    }
}
