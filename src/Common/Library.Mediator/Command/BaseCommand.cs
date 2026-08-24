namespace Library.Mediator.Command;

/// <summary>
///     Base type for all commands. A command represents an intent to change state (create, update,
///     delete) and is dispatched through the <see cref="CommandMediator" /> to its handler.
/// </summary>
/// <remarks>
///     Derive from this type to define a command, exposing the data the handler needs as properties.
///     Commands are simple data carriers without behavior.
/// </remarks>
public abstract class BaseCommand;
