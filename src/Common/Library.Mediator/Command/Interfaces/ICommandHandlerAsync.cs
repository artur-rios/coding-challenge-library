using Library.Output;

namespace Library.Mediator.Command.Interfaces;

/// <summary>Handles a command asynchronously, producing a typed output.</summary>
/// <remarks>
///     Implement this interface for each command and register the implementation in the dependency
///     injection container so the <see cref="CommandMediator" /> can resolve it. Register one handler
///     per command/output pair.
/// </remarks>
/// <typeparam name="TCommand">The command type handled by this handler.</typeparam>
/// <typeparam name="TOutput">The type of the data payload produced by this handler.</typeparam>
public interface ICommandHandlerAsync<in TCommand, TOutput>
    where TCommand : BaseCommand
    where TOutput : CommandOutput
{
    /// <summary>Executes the command asynchronously and returns its result.</summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">A token observed for cancellation, propagated to every awaited call.</param>
    Task<DataOutput<TOutput?>> HandleAsync(TCommand command, CancellationToken ct = default);
}
