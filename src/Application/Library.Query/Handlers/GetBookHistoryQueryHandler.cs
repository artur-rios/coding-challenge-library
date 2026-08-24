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
///     Lists every loan ever made of one book. Returned and cancelled loans are included by default -
///     they are the history, and an endpoint that quietly omitted them would make the deletions this
///     API refuses to perform happen anyway, one query at a time.
/// </summary>
public class GetBookHistoryQueryHandler(
    IValidator<GetBookHistoryQuery> validator,
    IAsyncReadOnlyRepository<Book> bookReader,
    IAsyncReadOnlyRepository<Loan> loanReader)
    : IPaginatedQueryHandlerAsync<GetBookHistoryQuery, LoanOutput>
{
    public async Task<PaginatedOutput<LoanOutput>> HandleAsync(
        GetBookHistoryQuery query, CancellationToken ct = default)
    {
        var validation = await validator.ValidateAsync(query, ct);

        if (!validation.IsValid)
        {
            return PaginatedOutput<LoanOutput>.New
                .WithErrors(validation.Errors.Select(failure => failure.ErrorMessage));
        }

        // Resolved first, so a book that does not exist is a 404 rather than an empty page - which
        // would be indistinguishable from a book that has never been lent.
        var bookId = await bookReader.Query().AsNoTracking()
            .Where(entity => entity.PublicId == query.BookId)
            .Select(entity => (long?)entity.Id)
            .FirstOrDefaultAsync(ct);

        if (bookId is null)
        {
            return PaginatedOutput<LoanOutput>.New.WithError(BookMessages.BookNotFound);
        }

        var loans = loanReader.Query().AsNoTracking().Where(loan => loan.BookId == bookId);

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

        // Newest first, which is what somebody opening a history wants; the identifier breaks ties
        // between loans made in the same instant so paging stays stable.
        var page = await projected
            .OrderByDescending(loan => loan.LoanedAt)
            .ThenBy(loan => loan.Id)
            .PaginateAsync(query.PageNumber, query.PageSize, ct);

        return page.WithMessage(BookMessages.HistoryRetrievedSuccessfully);
    }
}
