namespace Library.Shared.Messages;

/// <summary>Canonical messages produced when the audit trail is read.</summary>
public static class AuditMessages
{
    /// <summary>A page of audit entries was retrieved.</summary>
    public const string AuditEventsRetrievedSuccessfully = "Audit events retrieved successfully.";

    /// <summary>The submitted date range ends before it begins.</summary>
    public const string InvalidDateRange = "The end of the range must not precede its start.";

    /// <summary>The submitted entity type is not one the trail records.</summary>
    public const string InvalidEntityType = "Entity type must be one of: book, user, loan.";
}
