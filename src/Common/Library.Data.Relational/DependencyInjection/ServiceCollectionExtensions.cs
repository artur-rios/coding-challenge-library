using Library.Data.Relational.Configuration;
using Library.Data.Relational.Interfaces;
using Library.Data.Relational.Repositories;
using Library.Data.Relational.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Data.Relational.DependencyInjection;

/// <summary>Registers the generic repository and unit-of-work over an application's context.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    ///     Registers <typeparamref name="TContext" /> as the resolvable <see cref="BaseDbContext" />
    ///     and wires the open-generic repositories and the unit of work onto it.
    /// </summary>
    /// <remarks>
    ///     The repositories are registered open-generic, so a handler asks for
    ///     <c>IAsyncReadOnlyRepository&lt;Book&gt;</c> and gets one without a per-entity registration.
    ///     <typeparamref name="TContext" /> is also exposed as <see cref="BaseDbContext" /> so the one
    ///     unit-of-work registration serves every repository - all of them share that single context
    ///     instance within a scope, which is what makes a transaction opened by the unit of work
    ///     actually cover the repositories' writes.
    /// </remarks>
    public static IServiceCollection AddRelationalRepositories<TContext>(this IServiceCollection services)
        where TContext : BaseDbContext
    {
        services.AddScoped<BaseDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped(typeof(IAsyncRepository<>), typeof(EfRepository<>));
        services.AddScoped(typeof(IAsyncReadOnlyRepository<>), typeof(EfRepository<>));
        services.AddScoped<IAsyncUnitOfWork, EfUnitOfWork>();

        return services;
    }
}
