namespace Library.Mediator.Query;

/// <summary>
///     Base type for all queries. A query represents a read-only request for data and is dispatched
///     through the <see cref="QueryMediator" /> to its handler.
/// </summary>
/// <remarks>
///     <see cref="PageNumber" /> and <see cref="PageSize" /> are used by paginated query handlers and
///     ignored by the others.
/// </remarks>
public abstract class BaseQuery
{
    /// <summary>The 1-based page number to retrieve when the query is paginated. Defaults to <c>1</c>.</summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>The maximum number of items per page when the query is paginated. Defaults to <c>50</c>.</summary>
    public int PageSize { get; set; } = 50;
}
