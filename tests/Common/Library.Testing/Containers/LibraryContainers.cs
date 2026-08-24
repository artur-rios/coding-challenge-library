using Library.Data.Configuration;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace Library.Testing.Containers;

/// <summary>
///     Starts a throwaway PostgreSQL and Redis once for a test suite, applies the migrations, and
///     publishes the connection strings through the environment variables the API reads.
/// </summary>
/// <remarks>
///     <para>
///         Real containers rather than an in-memory provider, and that is the point rather than a
///         preference. Everything this API does about concurrency is expressed in PostgreSQL - atomic
///         conditional updates, a partial unique index, check constraints, row locks under
///         <c>READ COMMITTED</c>. An in-memory provider implements none of it and would report every
///         one of those tests as passing while proving nothing. The container costs about ten seconds
///         per suite and is the only thing that makes the concurrency test meaningful.
///     </para>
///     <para>
///         One container per suite, shared through a collection fixture: starting one per test class
///         multiplies that cost by the number of classes for no isolation the tests need, since each
///         test seeds its own books and readers with fresh identifiers.
///     </para>
/// </remarks>
public sealed class LibraryContainers : IAsyncLifetime
{
    // Pinned images, and the same major versions Compose runs. A suite that silently followed
    // "latest" would report a passing build on a database the deployment does not use.
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();

    /// <summary>The connection string of the running PostgreSQL container.</summary>
    public string PostgresConnectionString => _postgres.GetConnectionString();

    /// <summary>The connection string of the running Redis container.</summary>
    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        // Started together: they are independent, and the suite waits for the slower of the two
        // rather than for their sum.
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        // Published as environment variables because that is how the API is configured - so the host
        // under test resolves its dependencies through exactly the path a deployment does, rather
        // than through a test-only override that could hide a configuration bug.
        Environment.SetEnvironmentVariable(
            Data.DependencyInjection.ServiceCollectionExtensions.ConnectionStringVariable,
            PostgresConnectionString);

        Environment.SetEnvironmentVariable(
            Cache.DependencyInjection.ServiceCollectionExtensions.ConnectionStringVariable,
            RedisConnectionString);

        // The API is started with migrations off (a replica must not race its peers), so the suite
        // applies them itself - once, here.
        await using var context = CreateContext();

        await context.Database.MigrateAsync();
    }

    /// <summary>
    ///     Creates a context bound to the container, for tests that assert on database state directly
    ///     rather than through the API.
    /// </summary>
    public AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(PostgresConnectionString, Data.Configuration.NpgsqlConfiguration.Apply)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, DbContextDiagnosticsOptions.Disabled);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
