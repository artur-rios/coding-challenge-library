using System.Text.Json.Serialization;
using Library.Mediator.Command;

namespace Library.Command.Input;

/// <summary>Intent to lend one copy of a book to a reader.</summary>
public class CreateLoanCommand : BaseCommand
{
    /// <summary>Public identifier of the book to lend.</summary>
    public Guid BookId { get; set; }

    /// <summary>Public identifier of the reader borrowing it.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    ///     How long the reader may keep the copy, in days. Defaults to the library's standard period
    ///     when the caller does not say.
    /// </summary>
    public int LoanDays { get; set; } = DefaultLoanDays;

    /// <summary>
    ///     The <c>Idempotency-Key</c> header, applied by the controller rather than bound from the
    ///     body - it is a property of the request, not of the loan being asked for.
    /// </summary>
    [JsonIgnore]
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>The standard lending period.</summary>
    public const int DefaultLoanDays = 14;

    /// <summary>
    ///     The part of the command the idempotency fingerprint is taken over: what was asked for,
    ///     without the key that asked for it.
    /// </summary>
    /// <remarks>
    ///     The key is excluded deliberately - it is the lookup, so including it in its own fingerprint
    ///     would make every comparison trivially true and defeat the reuse check.
    /// </remarks>
    public object FingerprintPayload() => new { BookId, UserId, LoanDays };
}
