using Library.Mediator.Query;

namespace Library.Query.Input;

/// <summary>Lists the catalogue, with optional filters.</summary>
public class ListBooksQuery : BaseQuery
{
    /// <summary>Case-insensitive substring of the title.</summary>
    public string? Title { get; set; }

    /// <summary>Case-insensitive substring of the author.</summary>
    public string? Author { get; set; }

    /// <summary>
    ///     Whether deactivated books are included. Excluded by default: a caller listing the catalogue
    ///     usually wants what can be borrowed, and the withdrawn titles are still reachable by asking
    ///     for them.
    /// </summary>
    public bool IncludeInactive { get; set; }
}
