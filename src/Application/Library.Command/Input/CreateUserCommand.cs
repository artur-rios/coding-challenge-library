using Library.Mediator.Command;

namespace Library.Command.Input;

/// <summary>Intent to register a reader.</summary>
public class CreateUserCommand : BaseCommand
{
    /// <summary>Reader name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>E-mail address; normalised to lower case before it is stored and compared.</summary>
    public string Email { get; set; } = string.Empty;
}
