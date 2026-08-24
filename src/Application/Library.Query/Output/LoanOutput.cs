using Library.Domain.Enums;
using Library.Mediator.Query;

namespace Library.Query.Output;

/// <summary>A loan as the history endpoints report it.</summary>
public class LoanOutput : QueryOutput
{
    /// <summary>The loan's public identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Public identifier of the borrowed book.</summary>
    public Guid BookId { get; set; }

    /// <summary>The book's title, so a history reads without a join on the client's side.</summary>
    public string BookTitle { get; set; } = string.Empty;

    /// <summary>Public identifier of the borrowing reader.</summary>
    public Guid UserId { get; set; }

    /// <summary>The reader's name, for the same reason.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Where the loan stands.</summary>
    public LoanStatus Status { get; set; }

    /// <summary>The state name, so a client reading JSON does not have to know the numbers.</summary>
    public string StatusName => Status.ToString();

    /// <summary>When the copy was lent, UTC.</summary>
    public DateTime LoanedAt { get; set; }

    /// <summary>When the copy is due back, UTC.</summary>
    public DateTime DueAt { get; set; }

    /// <summary>When the copy came back, UTC; <c>null</c> if it has not.</summary>
    public DateTime? ReturnedAt { get; set; }

    /// <summary>When the loan was cancelled, UTC; <c>null</c> if it was not.</summary>
    public DateTime? CancelledAt { get; set; }
}
