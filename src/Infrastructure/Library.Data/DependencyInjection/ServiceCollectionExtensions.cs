using Library.Data.Availability;
using Library.Data.Configuration;
using Library.Data.Idempotency;
using Library.Data.Loans;
using Library.Data.Relational.DependencyInjection;
using Library.Shared.Availability;
using Library.Shared.Idempotency;
using Library.Shared.Loans;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Data.DependencyInjection;

/// <summary>Wires PostgreSQL, the context, the repositories and the three conditional-update writers.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The environment variable the connection string is read from.</summary>
    public const string ConnectionStringVariable = DesignTimeDbContextFactory.ConnectionStringVariable;

    /// <summary>
    ///     Registers everything the application needs to talk to PostgreSQL.
    /// </summary>
    /// <param name="services">The container.</param>
    /// <param name="connectionString">
    ///     The connection string; when omitted it is read from <see cref="ConnectionStringVariable" />.
    /// </param>
    /// <param name="diagnostics">Whether EF Core logs parameter values and detailed errors.</param>
    /// <exception cref="InvalidOperationException">No connection string was supplied or configured.</exception>
    public static IServiceCollection AddLibraryData(
        this IServiceCollection services,
        string? connectionString = null,
        DbContextDiagnosticsOptions? diagnostics = null)
    {
        var resolved = connectionString
                       ?? Environment.GetEnvironmentVariable(ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(resolved))
        {
            // Refused loudly at start-up rather than allowed to fail on the first request: a
            // misconfigured deployment should not pass its readiness probe and start taking traffic.
            throw new InvalidOperationException(
                $"Environment variable '{ConnectionStringVariable}' is unset. The API cannot start " +
                "without a database connection string.");
        }

        services.AddSingleton(diagnostics ?? DbContextDiagnosticsOptions.Disabled);

        services.AddDbContext<AppDbContext>(options => options
            // No EnableRetryOnFailure here, and that is a decision rather than an omission. The
            // retrying execution strategy refuses to run user-initiated transactions, and every write
            // in this API opens one - so switching it on would turn each loan into a runtime
            // exception. Retrying a transaction is also not a decision a provider should take on this
            // API's behalf: the caller retries with the same Idempotency-Key, which is exactly the
            // mechanism that makes a retry safe here.
            .UseNpgsql(resolved, NpgsqlConfiguration.Apply));

        services.AddRelationalRepositories<AppDbContext>();

        services.AddSingleton(IdempotencyOptions.FromEnvironment());
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IBookCopyReserver, BookCopyReserver>();
        services.AddScoped<ILoanTransitionWriter, LoanTransitionWriter>();

        return services;
    }
}
