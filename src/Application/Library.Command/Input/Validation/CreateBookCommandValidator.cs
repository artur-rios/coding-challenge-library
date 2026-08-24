using FluentValidation;
using Library.Shared.Catalog;
using Library.Shared.Messages;

namespace Library.Command.Input.Validation;

/// <summary>
///     Shape validation for <see cref="CreateBookCommand" />. ISBN uniqueness is the database's job -
///     it is the only place that can decide it without a race - so it is not checked here.
/// </summary>
public class CreateBookCommandValidator : AbstractValidator<CreateBookCommand>
{
    /// <summary>Upper bound on copies: large enough for any real library, small enough to catch a typo.</summary>
    public const int MaxTotalCopies = 100_000;

    public CreateBookCommandValidator()
    {
        RuleFor(command => command.Title)
            .NotEmpty().WithMessage(BookMessages.TitleRequired)
            .MaximumLength(300).WithMessage(BookMessages.TitleTooLong);

        RuleFor(command => command.Author)
            .NotEmpty().WithMessage(BookMessages.AuthorRequired)
            .MaximumLength(200).WithMessage(BookMessages.AuthorTooLong);

        RuleFor(command => command.Isbn)
            .NotEmpty().WithMessage(BookMessages.IsbnRequired)
            .Must(Isbn.IsValid).WithMessage(BookMessages.IsbnInvalid)
            .When(command => !string.IsNullOrWhiteSpace(command.Isbn));

        RuleFor(command => command.TotalCopies)
            .GreaterThanOrEqualTo(0).WithMessage(BookMessages.TotalCopiesNegative)
            .LessThanOrEqualTo(MaxTotalCopies).WithMessage(BookMessages.TotalCopiesTooLarge);
    }
}
