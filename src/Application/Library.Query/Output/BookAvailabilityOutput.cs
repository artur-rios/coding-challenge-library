using Library.Mediator.Query;

namespace Library.Query.Output;

/// <summary>What <c>GET /books/{id}/availability</c> reports.</summary>
public class BookAvailabilityOutput : QueryOutput
{
    /// <summary>The book's public identifier.</summary>
    public Guid BookId { get; set; }

    /// <summary>Title, so the answer is readable without a second call.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>How many copies the library owns.</summary>
    public int TotalCopies { get; set; }

    /// <summary>How many are on the shelf.</summary>
    public int AvailableCopies { get; set; }

    /// <summary>How many are out on loan right now.</summary>
    public int ActiveLoans { get; set; }

    /// <summary>Whether the book may still be lent.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    ///     Whether a loan would be accepted right now: the book is active and at least one copy is
    ///     free.
    /// </summary>
    /// <remarks>
    ///     Advisory, and deliberately labelled as such. This value may come from the cache and is a
    ///     statement about a moment that has already passed; the authority on whether a copy can be
    ///     taken is the conditional update inside <c>POST /loans</c>, which is the only place the
    ///     question is asked and answered atomically.
    /// </remarks>
    public bool CanBeLoaned => IsActive && AvailableCopies > 0;

    /// <summary>When this answer was computed from the database, UTC.</summary>
    /// <remarks>
    ///     Present so a caller can see the age of a cached answer rather than having to assume it is
    ///     current.
    /// </remarks>
    public DateTime CalculatedAt { get; set; }
}
