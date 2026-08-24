namespace Library.Mediator.Query;

/// <summary>
///     Base type for the data payload returned by a query handler. Depending on the handler, the
///     payload is wrapped either in a <see cref="Library.Output.DataOutput{T}" /> (single result) or a
///     <see cref="Library.Output.PaginatedOutput{T}" /> (paginated collection).
/// </summary>
public abstract class QueryOutput;
