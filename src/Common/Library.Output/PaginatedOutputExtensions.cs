using Microsoft.EntityFrameworkCore;

namespace Library.Output;

/// <summary>Turns a composed <see cref="IQueryable{T}" /> into one page of results.</summary>
public static class PaginatedOutputExtensions
{
    /// <summary>
    ///     Counts the query and materialises the requested page from it, returning both in a
    ///     <see cref="PaginatedOutput{T}" />.
    /// </summary>
    /// <remarks>
    ///     The caller is expected to have ordered the query already, and to have ordered it by
    ///     something unique. PostgreSQL gives no ordering guarantee between rows whose sort key ties,
    ///     and each page is a separate query free to break the tie differently - so a title shared by
    ///     two books straddling a page boundary could otherwise appear on both pages while another
    ///     book appeared on neither.
    /// </remarks>
    public static async Task<PaginatedOutput<T>> PaginateAsync<T>(
        this IQueryable<T> query, int pageNumber, int pageSize, CancellationToken ct = default)
    {
        var totalItems = await query.CountAsync(ct);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return PaginatedOutput<T>.New
            .WithData(items)
            .WithPagination(pageNumber, pageSize, totalItems);
    }
}
