using FluentValidation;
using Library.Mediator.Query;
using Library.Shared.Messages;

namespace Library.Query.Input.Validation;

/// <summary>
///     The page-number and page-size rules every paginated query shares, stated once so a new listing
///     cannot accidentally ship without them.
/// </summary>
/// <typeparam name="TQuery">The query being validated.</typeparam>
public abstract class PaginatedQueryValidator<TQuery> : AbstractValidator<TQuery> where TQuery : BaseQuery
{
    /// <summary>Largest page this API will build.</summary>
    public const int MaxPageSize = 200;

    /// <summary>Longest a filter value may be.</summary>
    protected const int MaxFilterLength = 300;

    protected PaginatedQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1).WithMessage(PaginationMessages.InvalidPageNumber);

        // Bounded above, not only below. An unbounded page size lets one request ask the database to
        // materialise the whole table and the API to serialize it, which is a denial of service that
        // needs no malice to trigger - a client with a default of int.MaxValue is enough.
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, MaxPageSize).WithMessage(PaginationMessages.InvalidPageSize);
    }
}
