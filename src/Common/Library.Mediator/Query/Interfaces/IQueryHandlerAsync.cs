using Library.Output;

namespace Library.Mediator.Query.Interfaces;

/// <summary>Handles a query asynchronously, returning a single typed result.</summary>
/// <typeparam name="TQuery">The query type handled by this handler.</typeparam>
/// <typeparam name="TOutput">The type of the data payload produced by this handler.</typeparam>
public interface IQueryHandlerAsync<in TQuery, TOutput>
    where TQuery : BaseQuery
    where TOutput : QueryOutput
{
    /// <summary>Executes the query asynchronously and returns its result.</summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="ct">A token observed for cancellation, propagated to every awaited call.</param>
    Task<DataOutput<TOutput?>> HandleAsync(TQuery query, CancellationToken ct = default);
}
