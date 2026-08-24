using Library.Mediator.Command.Interfaces;
using Library.Output;
using Microsoft.Extensions.DependencyInjection;

namespace Library.Mediator.Command;

/// <summary>
///     Dispatches commands to their registered handlers. For each call a new dependency injection
///     scope is created, the matching handler is resolved from it, and the handler is invoked.
/// </summary>
/// <remarks>
///     Because a fresh scope is created per execution, scoped dependencies of a handler - the
///     <c>DbContext</c>, above all - are isolated to a single command execution. That matters here
///     more than usual: loan creation opens its own transaction, and sharing a context with an
///     unrelated request would put that transaction on somebody else's connection.
/// </remarks>
/// <param name="scopeFactory">The factory used to create a scope for resolving handlers.</param>
public class CommandMediator(IServiceScopeFactory scopeFactory)
{
    /// <summary>Resolves the handler for the given command and executes it.</summary>
    /// <typeparam name="TCommand">The command type to execute.</typeparam>
    /// <typeparam name="TOutput">The type of the data payload produced by the handler.</typeparam>
    /// <param name="command">The command instance to execute.</param>
    /// <param name="ct">A token observed for cancellation.</param>
    /// <exception cref="InvalidOperationException">
    ///     No <see cref="ICommandHandlerAsync{TCommand,TOutput}" /> is registered for the requested
    ///     command and output types.
    /// </exception>
    public async Task<DataOutput<TOutput?>> ExecuteCommandAsync<TCommand, TOutput>(
        TCommand command, CancellationToken ct = default)
        where TCommand : BaseCommand
        where TOutput : CommandOutput
    {
        using var scoped = scopeFactory.CreateScope();

        var handler = scoped.ServiceProvider.GetRequiredService<ICommandHandlerAsync<TCommand, TOutput>>();

        return await handler.HandleAsync(command, ct).ConfigureAwait(false);
    }
}
