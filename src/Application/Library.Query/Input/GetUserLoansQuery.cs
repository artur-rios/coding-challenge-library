using Library.Domain.Enums;
using Library.Mediator.Query;

namespace Library.Query.Input;

/// <summary>Lists every loan ever made to one reader, in any state.</summary>
public class GetUserLoansQuery : BaseQuery
{
    /// <summary>The reader's public identifier.</summary>
    public Guid UserId { get; set; }

    /// <summary>Narrows the history to one state; omitted, every loan is returned.</summary>
    public LoanStatus? Status { get; set; }
}
