using System.ComponentModel.DataAnnotations;
using Library.Data.Relational.Entities;
using Library.Domain.Enums;

namespace Library.Domain.Entities;

/// <summary>
///     One copy of one book, out to one reader. Append-and-transition only: a loan is never deleted,
///     and neither returning nor cancelling erases anything - both write a terminal
///     <see cref="Status" /> and a timestamp beside the original one.
/// </summary>
public class Loan : Entity
{
    /// <summary>External identifier, used everywhere the loan is addressed from outside the database.</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>Foreign key to the borrowed <see cref="Entities.Book" /> (internal Id). Required.</summary>
    public long BookId { get; set; }

    /// <summary>Foreign key to the borrowing <see cref="Entities.User" /> (internal Id). Required.</summary>
    public long UserId { get; set; }

    /// <summary>Where the loan currently stands.</summary>
    public LoanStatus Status { get; set; } = LoanStatus.Active;

    /// <summary>When the copy was lent, UTC.</summary>
    public DateTime LoanedAt { get; set; }

    /// <summary>When the copy is expected back, UTC.</summary>
    public DateTime DueAt { get; set; }

    /// <summary>When the copy came back, UTC; <c>null</c> while the loan is not returned.</summary>
    public DateTime? ReturnedAt { get; set; }

    /// <summary>When the loan was cancelled, UTC; <c>null</c> unless it was.</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    ///     The <c>Idempotency-Key</c> the creating request carried, kept on the row itself.
    /// </summary>
    /// <remarks>
    ///     The idempotency store already maps a key to the response it produced; this is the same fact
    ///     recorded where an operator investigating a duplicate would look first - on the loan - and
    ///     it survives the store's own retention window.
    /// </remarks>
    [MaxLength(200)]
    public string? IdempotencyKey { get; set; }

    // Navigation properties

    /// <summary>The borrowed book.</summary>
    public Book Book { get; set; } = null!;

    /// <summary>The borrowing reader.</summary>
    public User User { get; set; } = null!;
}
