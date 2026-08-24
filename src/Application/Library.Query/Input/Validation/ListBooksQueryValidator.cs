using FluentValidation;
using Library.Shared.Messages;

namespace Library.Query.Input.Validation;

/// <summary>Pagination and filter-length validation for <see cref="ListBooksQuery" />.</summary>
public class ListBooksQueryValidator : PaginatedQueryValidator<ListBooksQuery>
{
    public ListBooksQueryValidator()
    {
        RuleFor(query => query.Title!)
            .MaximumLength(MaxFilterLength).WithMessage(PaginationMessages.FilterTooLong)
            .When(query => query.Title is not null);

        RuleFor(query => query.Author!)
            .MaximumLength(MaxFilterLength).WithMessage(PaginationMessages.FilterTooLong)
            .When(query => query.Author is not null);
    }
}
