using FluentValidation;
using Library.Command.Input;
using Library.Command.Output;
using Library.Data.Relational.Interfaces;
using Library.Data.Relational.Repositories;
using Library.Data.Relational.Transactions;
using Library.Domain.Entities;
using Library.Domain.Enums;
using Library.Mediator.Command.Interfaces;
using Library.Output;
using Library.Shared.Auditing;
using Library.Shared.Messages;

namespace Library.Command.Handlers;

/// <summary>Registers a reader, with the audit entry written in the same transaction.</summary>
public class CreateUserCommandHandler(
    IValidator<CreateUserCommand> validator,
    IAsyncRepository<User> userWriter,
    IAuditEventWriter auditWriter,
    IAsyncUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandlerAsync<CreateUserCommand, CreateUserCommandOutput>
{
    public async Task<DataOutput<CreateUserCommandOutput?>> HandleAsync(
        CreateUserCommand command, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(command, ct);

        if (!validation.IsValid)
        {
            return DataOutput<CreateUserCommandOutput?>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        var user = new User
        {
            PublicId = Guid.NewGuid(),
            Name = command.Name.Trim(),
            // Lower-cased on the way in, which is what makes the unique index mean "this person is
            // already registered" rather than "this exact casing is".
            Email = command.Email.Trim().ToLowerInvariant(),
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        return await unitOfWork.ExecuteInTransactionAsync<CreateUserCommandOutput>(async () =>
        {
            var output = DataOutput<CreateUserCommandOutput?>.New;

            var creation = await userWriter.CreateAsync(user, ct);

            if (!creation.Success)
            {
                return output.WithErrors(creation.Errors.Select(error =>
                    error == RelationalErrors.UniqueViolationMessage
                        ? UserMessages.EmailAlreadyRegistered
                        : error));
            }

            await auditWriter.WriteAsync(
                AuditEntityTypes.User, user.PublicId, AuditActions.UserCreated,
                new { user.Name, user.Email }, ct);

            return output
                .WithData(new CreateUserCommandOutput
                {
                    Id = user.PublicId,
                    Name = user.Name,
                    Email = user.Email,
                    CreatedAt = user.CreatedAt
                })
                .WithMessage(UserMessages.UserCreatedSuccessfully);
        }, ct: ct);
    }
}
