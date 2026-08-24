using FluentValidation;
using Library.Data.Relational.Interfaces;
using Library.Domain.Entities;
using Library.Mediator.Query.Interfaces;
using Library.Output;
using Library.Query.Input;
using Library.Query.Output;
using Library.Shared.Messages;
using Microsoft.EntityFrameworkCore;

namespace Library.Query.Handlers;

/// <summary>
///     Lists every loan ever made to one reader, in any state, newest first.
/// </summary>
public class GetUserLoansQueryHandler(
    IValidator<GetUserLoansQuery> validator,
    IAsyncReadOnlyRepository<User> userReader,
    IAsyncReadOnlyRepository<Loan> loanReader)
    : IPaginatedQueryHandlerAsync<GetUserLoansQuery, LoanOutput>
{
    public async Task<PaginatedOutput<LoanOutput>> HandleAsync(
        GetUserLoansQuery query, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(query, ct);

        if (!validation.IsValid)
        {
            return PaginatedOutput<LoanOutput>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        // Resolved first, so an unknown reader is a 404 rather than an empty page indistinguishable
        // from a reader who has never borrowed anything.
        var userId = await userReader.Query().AsNoTracking()
            .Where(entity => entity.PublicId == query.UserId)
            .Select(entity => (long?)entity.Id)
            .FirstOrDefaultAsync(ct);

        if (userId is null)
        {
            return PaginatedOutput<LoanOutput>.New.WithError(UserMessages.UserNotFound);
        }

        var loans = loanReader.Query().AsNoTracking().Where(loan => loan.UserId == userId);

        if (query.Status is { } status)
        {
            loans = loans.Where(loan => loan.Status == status);
        }

        var projected = loans.Select(loan => new LoanOutput
        {
            Id = loan.PublicId,
            BookId = loan.Book.PublicId,
            BookTitle = loan.Book.Title,
            UserId = loan.User.PublicId,
            UserName = loan.User.Name,
            Status = loan.Status,
            LoanedAt = loan.LoanedAt,
            DueAt = loan.DueAt,
            ReturnedAt = loan.ReturnedAt,
            CancelledAt = loan.CancelledAt
        });

        var page = await projected
            .OrderByDescending(loan => loan.LoanedAt)
            .ThenBy(loan => loan.Id)
            .PaginateAsync(query.PageNumber, query.PageSize, ct);

        return page.WithMessage(UserMessages.UserLoansRetrievedSuccessfully);
    }
}
