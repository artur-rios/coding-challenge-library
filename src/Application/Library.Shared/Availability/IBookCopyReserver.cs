namespace Library.Shared.Availability;

/// <summary>
///     Every change to a book's available-copy count, expressed as an atomic conditional update.
/// </summary>
/// <remarks>
///     <para>
///         This interface exists because the rule "you may take a copy only if one is there" cannot be
///         enforced by reading the count and then writing it back. Between the read and the write
///         another replica does the same thing, both see one copy, and both lend it. The fix is not to
///         read at all: each method issues a single <c>UPDATE ... WHERE</c> in which the condition
///         <em>is</em> the rule, and reports whether the database applied it. PostgreSQL takes a row
///         lock for the duration of that statement, so two concurrent attempts are serialised by the
///         database and exactly one of them finds the row still satisfying the condition.
///     </para>
///     <para>
///         The alternatives considered - <c>SELECT ... FOR UPDATE</c> then write, an optimistic token
///         with a retry loop, or <c>SERIALIZABLE</c> isolation - all produce correct results too. They
///         were not chosen because each costs an extra round trip, a retry loop, or a class of
///         serialization failures the caller has to handle, in exchange for nothing this operation
///         needs. The README records the comparison in full.
///     </para>
/// </remarks>
public interface IBookCopyReserver
{
    /// <summary>
    ///     Takes one copy of the book, if the book is active and a copy is available.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when a copy was taken; <c>false</c> when none was, which is the ordinary
    ///     "somebody else got the last one" outcome rather than a failure.
    /// </returns>
    Task<bool> TryReserveCopyAsync(long bookId, CancellationToken ct = default);

    /// <summary>
    ///     Puts one copy back, for a return or a cancellation.
    /// </summary>
    /// <returns>
    ///     <c>true</c> when a copy was returned to the shelf. <c>false</c> means the increment would
    ///     have pushed the available count past the total, which cannot happen while loans and counts
    ///     agree - the caller treats it as the inconsistency it is rather than ignoring it.
    /// </returns>
    Task<bool> TryReleaseCopyAsync(long bookId, CancellationToken ct = default);

    /// <summary>
    ///     Sets the total number of copies and moves the available count by the same delta.
    /// </summary>
    /// <returns>
    ///     <c>false</c> when the change would drive the available count below zero - which is what
    ///     happens when somebody tries to shrink the catalogue below the number of copies currently
    ///     out on loan.
    /// </returns>
    Task<bool> TryAdjustTotalCopiesAsync(long bookId, int totalCopies, CancellationToken ct = default);
}
