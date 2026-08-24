namespace Library.Shared.Messages;

/// <summary>Refusals every paginated listing shares.</summary>
public static class PaginationMessages
{
    /// <summary>The requested page number is below one.</summary>
    public const string InvalidPageNumber = "Page number must be 1 or greater.";

    /// <summary>
    ///     The requested page size is outside the accepted range. Bounded above as well as below: an
    ///     unbounded page size is a way to ask one request to materialise the whole table.
    /// </summary>
    public const string InvalidPageSize = "Page size must be between 1 and 200.";

    /// <summary>A filter value is longer than the column it filters.</summary>
    public const string FilterTooLong = "Filter values must be at most 300 characters.";
}
