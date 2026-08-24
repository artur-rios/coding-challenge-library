using FluentValidation;
using Library.Shared.Messages;

namespace Library.Command.Input.Validation;

/// <summary>
///     Shape validation for <see cref="CreateUserCommand" />. Uniqueness of the address is left to
///     the database's unique index, for the same reason the ISBN is.
/// </summary>
public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage(UserMessages.NameRequired)
            .MaximumLength(200).WithMessage(UserMessages.NameTooLong);

        RuleFor(command => command.Email)
            .NotEmpty().WithMessage(UserMessages.EmailRequired)
            .MaximumLength(320).WithMessage(UserMessages.EmailTooLong)
            .EmailAddress().WithMessage(UserMessages.EmailInvalid);
    }
}
