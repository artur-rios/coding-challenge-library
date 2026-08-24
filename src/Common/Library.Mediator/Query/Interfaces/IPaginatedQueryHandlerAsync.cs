using Library.Output;

namespace Library.Mediator.Query.Interfaces;

/// <summary>Handles a query asynchronously, returning a paginated collection of results.</summary>
/// <typeparam name="TQuery">The query type handled by this handler.</typeparam>
/// <typeparam name="TOutput">The type of the items in the paginated result.</typeparam>
public interface IPaginatedQueryHandlerAsync<in TQuery, TOutput>
    where TQuery : BaseQuery
    where TOutput : QueryOutput
{
    /// <summary>Executes the query asynchronously and returns a page of results.</summary>
    /// <param name="query">The query to execute, including the requested page metadata.</param>
    /// <param name="ct">A token observed for cancellation, propagated to every awaited call.</param>
    Task<PaginatedOutput<TOutput>> HandleAsync(TQuery query, CancellationToken ct = default);
}
