using Library.Domain.Enums;
using Library.Mediator.Command;

namespace Library.Command.Output;

/// <summary>The state of a loan after it was created, returned or cancelled.</summary>
public class LoanCommandOutput : CommandOutput
{
    /// <summary>The loan's public identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Public identifier of the borrowed book.</summary>
    public Guid BookId { get; set; }

    /// <summary>Public identifier of the borrowing reader.</summary>
    public Guid UserId { get; set; }

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

    /// <summary>How many copies of the book are on the shelf after this operation.</summary>
    public int AvailableCopies { get; set; }

    /// <summary>
    ///     Whether this response was replayed from the idempotency store rather than produced by
    ///     doing the work again.
    /// </summary>
    /// <remarks>
    ///     Not part of the stored body - it is set when the stored body is replayed, so the loan a
    ///     caller sees is byte-for-byte the one they saw the first time, plus this flag telling them
    ///     which of the two calls they are looking at.
    /// </remarks>
    public bool Replayed { get; set; }
}
