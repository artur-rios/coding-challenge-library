using Library.Mediator.Command;

namespace Library.Command.Input;

/// <summary>Intent to add a title to the catalogue.</summary>
public class CreateBookCommand : BaseCommand
{
    /// <summary>Book title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>ISBN-10 or ISBN-13, in any punctuation; it is normalised before it is stored.</summary>
    public string Isbn { get; set; } = string.Empty;

    /// <summary>Author name.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>
    ///     How many copies the library owns. All of them start on the shelf, so this is also the
    ///     initial available count.
    /// </summary>
    public int TotalCopies { get; set; }
}
