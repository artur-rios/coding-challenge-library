using System.Text.Json.Serialization;
using Library.Mediator.Command;

namespace Library.Command.Input;

/// <summary>
///     Intent to withdraw a book from lending. This is what <c>DELETE /books/{id}</c> does: no book
///     with a history is ever removed, so the operation deactivates instead.
/// </summary>
public class DeactivateBookCommand : BaseCommand
{
    /// <summary>The book's public identifier, taken from the route.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }
}
