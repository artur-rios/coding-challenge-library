using System.Text.Json.Serialization;
using Library.Mediator.Command;

namespace Library.Command.Input;

/// <summary>
///     Intent to change a book's details. Every field is nullable and an omitted one is left alone -
///     these are <c>PATCH</c> semantics, so a request that sends only a title must not blank the
///     author.
/// </summary>
public class UpdateBookCommand : BaseCommand
{
    /// <summary>The book's public identifier, taken from the route rather than the body.</summary>
    [JsonIgnore]
    public Guid Id { get; set; }

    /// <summary>New title, or <c>null</c> to leave it as it is.</summary>
    public string? Title { get; set; }

    /// <summary>New ISBN, or <c>null</c> to leave it as it is.</summary>
    public string? Isbn { get; set; }

    /// <summary>New author, or <c>null</c> to leave it as it is.</summary>
    public string? Author { get; set; }

    /// <summary>
    ///     New total number of copies, or <c>null</c> to leave it as it is. The available count moves
    ///     by the same delta, and the change is refused if that would drive it below zero.
    /// </summary>
    public int? TotalCopies { get; set; }
}
