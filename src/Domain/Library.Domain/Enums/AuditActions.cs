namespace Library.Domain.Enums;

/// <summary>
///     The business actions the audit trail records. Strings rather than an enum: the trail is read
///     by people and by tools that never compile against this assembly, and a stored <c>3</c> whose
///     meaning lives in a since-edited enum is exactly the kind of entry an audit trail must not
///     contain.
/// </summary>
public static class AuditActions
{
    /// <summary>A book was added to the catalogue.</summary>
    public const string BookCreated = "book.created";

    /// <summary>A book's metadata or copy count changed.</summary>
    public const string BookUpdated = "book.updated";

    /// <summary>A book was deactivated - withdrawn from lending, never erased.</summary>
    public const string BookDeactivated = "book.deactivated";

    /// <summary>A reader was registered.</summary>
    public const string UserCreated = "user.created";

    /// <summary>A copy was lent out.</summary>
    public const string LoanCreated = "loan.created";

    /// <summary>A copy came back.</summary>
    public const string LoanReturned = "loan.returned";

    /// <summary>A loan was cancelled, and kept.</summary>
    public const string LoanCancelled = "loan.cancelled";
}
