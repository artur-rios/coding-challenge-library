using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Library.Data.Configuration;

/// <summary>
///     Builds an <see cref="AppDbContext" /> for the EF Core command-line tools, which have no access
///     to the application's dependency-injection container.
/// </summary>
/// <remarks>
///     The connection string comes from <c>LIBRARY_DATA_CONNECTIONSTRING</c>, with a localhost default
///     so <c>dotnet ef migrations add</c> works on a developer machine without any setup. Diagnostics
///     are off: design time never needs them, and the tools may well be pointed at production.
/// </remarks>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>The environment variable the connection string is read from.</summary>
    public const string ConnectionStringVariable = "LIBRARY_DATA_CONNECTIONSTRING";

    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=library;Username=postgres;Password=postgres";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? DefaultConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            // The same provider settings the host uses, so design time and run time agree on where
            // the migrations history table lives.
            .UseNpgsql(connectionString, NpgsqlConfiguration.Apply)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AppDbContext(options, DbContextDiagnosticsOptions.Disabled);
    }
}
