using FluentValidation;
using Library.Domain.Enums;
using Library.Shared.Messages;

namespace Library.Query.Input.Validation;

/// <summary>Pagination, range and filter validation for <see cref="ListAuditEventsQuery" />.</summary>
public class ListAuditEventsQueryValidator : PaginatedQueryValidator<ListAuditEventsQuery>
{
    private static readonly string[] KnownEntityTypes =
        [AuditEntityTypes.Book, AuditEntityTypes.User, AuditEntityTypes.Loan];

    public ListAuditEventsQueryValidator()
    {
        RuleFor(query => query.EntityType!)
            .Must(entityType => KnownEntityTypes.Contains(entityType.ToLowerInvariant()))
            .WithMessage(AuditMessages.InvalidEntityType)
            .When(query => !string.IsNullOrWhiteSpace(query.EntityType));

        RuleFor(query => query.Action!)
            .MaximumLength(MaxFilterLength).WithMessage(PaginationMessages.FilterTooLong)
            .When(query => query.Action is not null);

        RuleFor(query => query.Actor!)
            .MaximumLength(MaxFilterLength).WithMessage(PaginationMessages.FilterTooLong)
            .When(query => query.Actor is not null);

        RuleFor(query => query.CorrelationId!)
            .MaximumLength(MaxFilterLength).WithMessage(PaginationMessages.FilterTooLong)
            .When(query => query.CorrelationId is not null);

        // An inverted range returns nothing and looks identical to "no such events", which is exactly
        // the wrong answer to give somebody investigating an incident.
        RuleFor(query => query)
            .Must(query => query.From is null || query.To is null || query.From <= query.To)
            .WithMessage(AuditMessages.InvalidDateRange);
    }
}
