using Library.Mediator.Query;

namespace Library.Query.Output;

/// <summary>A book as the catalogue endpoints report it.</summary>
public class BookOutput : QueryOutput
{
    /// <summary>The book's public identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>ISBN, in the normalised form that is stored.</summary>
    public string Isbn { get; set; } = string.Empty;

    /// <summary>Author.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>How many copies the library owns.</summary>
    public int TotalCopies { get; set; }

    /// <summary>How many are on the shelf.</summary>
    public int AvailableCopies { get; set; }

    /// <summary>Whether the book may still be lent.</summary>
    public bool IsActive { get; set; }

    /// <summary>Creation timestamp, UTC.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last update timestamp, UTC.</summary>
    public DateTime UpdatedAt { get; set; }
}
