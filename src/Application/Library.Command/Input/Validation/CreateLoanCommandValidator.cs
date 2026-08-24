using FluentValidation;
using Library.Shared.Messages;

namespace Library.Command.Input.Validation;

/// <summary>
///     Shape validation for <see cref="CreateLoanCommand" />, including the idempotency key - which
///     is required by this endpoint rather than optional, so its absence is an input error like any
///     other.
/// </summary>
public class CreateLoanCommandValidator : AbstractValidator<CreateLoanCommand>
{
    /// <summary>Shortest loan the library issues.</summary>
    public const int MinLoanDays = 1;

    /// <summary>Longest loan the library issues.</summary>
    public const int MaxLoanDays = 90;

    public CreateLoanCommandValidator()
    {
        RuleFor(command => command.BookId)
            .NotEmpty().WithMessage(LoanMessages.BookIdRequired);

        RuleFor(command => command.UserId)
            .NotEmpty().WithMessage(LoanMessages.UserIdRequired);

        RuleFor(command => command.LoanDays)
            .InclusiveBetween(MinLoanDays, MaxLoanDays).WithMessage(LoanMessages.LoanPeriodOutOfRange);

        RuleFor(command => command.IdempotencyKey)
            .NotEmpty().WithMessage(LoanMessages.IdempotencyKeyRequired)
            .MaximumLength(200).WithMessage(LoanMessages.IdempotencyKeyTooLong);
    }
}
