namespace Library.Domain.Enums;

/// <summary>The entity kinds the audit trail names, for the same reason <see cref="AuditActions" /> are strings.</summary>
public static class AuditEntityTypes
{
    /// <summary>A catalogue book.</summary>
    public const string Book = "book";

    /// <summary>A registered reader.</summary>
    public const string User = "user";

    /// <summary>A loan of one copy.</summary>
    public const string Loan = "loan";
}
