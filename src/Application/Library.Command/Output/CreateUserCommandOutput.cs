using Library.Mediator.Command;

namespace Library.Command.Output;

/// <summary>The reader that was registered.</summary>
public class CreateUserCommandOutput : CommandOutput
{
    /// <summary>The reader's public identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>E-mail address, in the normalised form that is stored.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Registration timestamp, UTC.</summary>
    public DateTime CreatedAt { get; set; }
}
