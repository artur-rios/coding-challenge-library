using System.Text.Json.Serialization;
using Library.Mediator.Command;

namespace Library.Command.Input;

/// <summary>
///     Intent to call off a loan. The row stays and takes a terminal state, so the fact that the loan
///     existed - and was cancelled rather than returned - is still there afterwards.
/// </summary>
public class CancelLoanCommand : BaseCommand
{
    /// <summary>The loan's public identifier, taken from the route.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>Why it was cancelled, recorded on the audit entry. Optional.</summary>
    public string? Reason { get; set; }
}
