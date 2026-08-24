namespace Library.Mediator.Command;

/// <summary>
///     Base type for the data payload returned by a command handler. The payload is wrapped in a
///     <see cref="Library.Output.DataOutput{T}" /> so callers also receive success state, messages
///     and errors alongside the data.
/// </summary>
public abstract class CommandOutput;
