using FluentValidation;
using Library.Shared.Catalog;
using Library.Shared.Messages;

namespace Library.Command.Input.Validation;

/// <summary>
///     Shape validation for <see cref="UpdateBookCommand" />. Each rule is conditional on the field
///     having been supplied, because an omitted field means "leave it alone" and must not be read as
///     an empty one.
/// </summary>
public class UpdateBookCommandValidator : AbstractValidator<UpdateBookCommand>
{
    public UpdateBookCommandValidator()
    {
        // A request that supplies nothing is refused rather than treated as a no-op success: it is
        // almost always a client that serialized the wrong object, and answering 200 hides that.
        RuleFor(command => command)
            .Must(command =>
                command.Title is not null || command.Isbn is not null ||
                command.Author is not null || command.TotalCopies is not null)
            .WithMessage(BookMessages.NoFieldsToUpdate);

        When(command => command.Title is not null, () =>
            RuleFor(command => command.Title!)
                .NotEmpty().WithMessage(BookMessages.TitleRequired)
                .MaximumLength(300).WithMessage(BookMessages.TitleTooLong));

        When(command => command.Author is not null, () =>
            RuleFor(command => command.Author!)
                .NotEmpty().WithMessage(BookMessages.AuthorRequired)
                .MaximumLength(200).WithMessage(BookMessages.AuthorTooLong));

        When(command => command.Isbn is not null, () =>
            RuleFor(command => command.Isbn!)
                .NotEmpty().WithMessage(BookMessages.IsbnRequired)
                .Must(Isbn.IsValid).WithMessage(BookMessages.IsbnInvalid));

        When(command => command.TotalCopies is not null, () =>
            RuleFor(command => command.TotalCopies!.Value)
                .GreaterThanOrEqualTo(0).WithMessage(BookMessages.TotalCopiesNegative)
                .LessThanOrEqualTo(CreateBookCommandValidator.MaxTotalCopies)
                .WithMessage(BookMessages.TotalCopiesTooLarge));
    }
}
