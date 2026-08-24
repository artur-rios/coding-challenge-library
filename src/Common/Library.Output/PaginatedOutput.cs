namespace Library.Output;

/// <summary>Represents a paginated collection result and its pagination metadata.</summary>
/// <typeparam name="T">Type of the items in the paginated collection.</typeparam>
public class PaginatedOutput<T> : DataOutput<List<T>>
{
    /// <summary>Current page number (1-based).</summary>
    public int PageNumber { get; set; }

    /// <summary>
    ///     Number of items requested per page. This is the requested page size, not the number of
    ///     items actually present in <see cref="DataOutput{T}.Data" />, which may be smaller on the
    ///     last page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>Total number of items across all pages.</summary>
    public int TotalItems { get; set; }

    /// <summary>
    ///     Total number of pages computed from <see cref="TotalItems" /> and <see cref="PageSize" />.
    ///     Evaluates to <c>0</c> when no page size has been set.
    /// </summary>
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalItems / PageSize);

    /// <summary>Creates a new <see cref="PaginatedOutput{T}" /> instance.</summary>
    public static new PaginatedOutput<T> New => new();

    /// <summary>Sets pagination metadata for the current instance.</summary>
    public PaginatedOutput<T> WithPagination(int pageNumber, int pageSize, int totalItems)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalItems = totalItems;

        return this;
    }

    /// <summary>Replaces the internal data list with the provided list and returns the instance.</summary>
    public new PaginatedOutput<T> WithData(List<T> data)
    {
        Data = data;

        return this;
    }

    /// <summary>Sets the internal data list to an empty list and returns the instance.</summary>
    public PaginatedOutput<T> WithEmptyData()
    {
        Data = [];

        return this;
    }

    /// <summary>Fluent helper to add an error on the current instance.</summary>
    public new PaginatedOutput<T> WithError(string error)
    {
        AddError(error);

        return this;
    }

    /// <summary>Fluent helper to add multiple errors on the current instance.</summary>
    public new PaginatedOutput<T> WithErrors(IEnumerable<string>? errors)
    {
        AddErrors(errors);

        return this;
    }

    /// <summary>Fluent helper to add an informational message on the current instance.</summary>
    public new PaginatedOutput<T> WithMessage(string message)
    {
        AddMessage(message);

        return this;
    }

    /// <summary>Fluent helper to add multiple informational messages on the current instance.</summary>
    public new PaginatedOutput<T> WithMessages(IEnumerable<string>? messages)
    {
        AddMessages(messages);

        return this;
    }
}
