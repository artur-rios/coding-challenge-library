using Library.Mediator.Query.Interfaces;
using Library.Output;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Mediator.Query;

/// <summary>
///     Dispatches queries to their registered handlers. For each call a new dependency injection
///     scope is created, the matching handler is resolved from it, and the handler is invoked.
///     Supports both single-result and paginated queries.
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope for resolving handlers.</param>
public class QueryMediator(IServiceScopeFactory scopeFactory)
{
    /// <summary>Resolves the single-result handler for the given query and executes it.</summary>
    /// <exception cref="InvalidOperationException">
    ///     No <see cref="IQueryHandlerAsync{TQuery,TOutput}" /> is registered for the requested types.
    /// </exception>
    public async Task<DataOutput<TOutput?>> ExecuteQueryAsync<TQuery, TOutput>(
        TQuery query, CancellationToken ct = default)
        where TQuery : BaseQuery
        where TOutput : QueryOutput
    {
        using var scoped = scopeFactory.CreateScope();

        var handler = scoped.ServiceProvider.GetRequiredService<IQueryHandlerAsync<TQuery, TOutput>>();

        return await handler.HandleAsync(query, ct).ConfigureAwait(false);
    }

    /// <summary>Resolves the paginated handler for the given query and executes it.</summary>
    /// <exception cref="InvalidOperationException">
    ///     No <see cref="IPaginatedQueryHandlerAsync{TQuery,TOutput}" /> is registered for the
    ///     requested types.
    /// </exception>
    public async Task<PaginatedOutput<TOutput>> ExecutePaginatedQueryAsync<TQuery, TOutput>(
        TQuery query, CancellationToken ct = default)
        where TQuery : BaseQuery
        where TOutput : QueryOutput
    {
        using var scoped = scopeFactory.CreateScope();

        var handler = scoped.ServiceProvider.GetRequiredService<IPaginatedQueryHandlerAsync<TQuery, TOutput>>();

        return await handler.HandleAsync(query, ct).ConfigureAwait(false);
    }
}
