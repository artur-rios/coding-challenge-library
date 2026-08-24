using System.Text.Json.Serialization;
using Library.Mediator.Command;

namespace Library.Command.Input;

/// <summary>Intent to record that a lent copy came back.</summary>
public class ReturnLoanCommand : BaseCommand
{
    /// <summary>The loan's public identifier, taken from the route.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }
}
