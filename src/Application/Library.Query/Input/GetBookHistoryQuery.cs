using Library.Domain.Enums;
using Library.Mediator.Query;

namespace Library.Query.Input;

/// <summary>Lists every loan ever made of one book, in any state.</summary>
public class GetBookHistoryQuery : BaseQuery
{
    /// <summary>The book's public identifier.</summary>
    public Guid BookId { get; set; }

    /// <summary>
    ///     Narrows the history to one state. Omitted, every loan is returned - which is the point of a
    ///     history endpoint, so returned and cancelled loans are never filtered out by default.
    /// </summary>
    public LoanStatus? Status { get; set; }
}
