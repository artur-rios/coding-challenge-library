using System.Net;
using Library.Command.Output;
using Library.Domain.Enums;
using Library.Output;
using Library.Query.Output;
using Library.Testing.Attributes;
using Library.Testing.Containers;
using Library.WebApi.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Library.WebApi.Tests;

// The audit requirement, end to end: every write the challenge names produces an entry carrying the
// entity, its identifier, the action, the actor, a UTC timestamp, a correlation identifier and
// enough detail to understand the change.
[Collection(nameof(FunctionalCollection))]
public class AuditTrailTests(LibraryContainers containers) : LibraryApiTest(containers)
{
    private const string ActorHeader = "X-Actor";
    private const string CorrelationHeader = "X-Correlation-Id";

    [FunctionalFact]
    public async Task GivenEveryAuditedOperation_WhenPerformingThem_ThenEachProducesAnEntry()
    {
        // Given a correlation identifier of our own, so the whole run can be pulled back out in one
        // query - which is what the identifier is for
        var correlationId = $"audit-{Guid.NewGuid():N}";
        var headers = new Dictionary<string, string>
        {
            [ActorHeader] = "librarian@example.com",
            [CorrelationHeader] = correlationId
        };

        var createBook = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "Audited Title", isbn = UniqueIsbn(), author = "Author", totalCopies = 2
        }, headers);

        var book = createBook.Body!.Data!;

        await Gateway.PatchAsync<DataOutput<BookCommandOutput>>(
            $"/books/{book.Id}", new { title = "Audited Title, Revised" });

        var createUser = await Gateway.PostAsync<DataOutput<CreateUserCommandOutput>>("/users", new
        {
            name = "Reader", email = $"reader-{Guid.NewGuid():N}@test.local"
        }, headers);

        var reader = createUser.Body!.Data!;

        var loan = await CreateLoanAsync(book.Id, reader.Id);
        var loanId = loan.Body!.Data!.Id;

        await Gateway.PostAsync<DataOutput<LoanCommandOutput>>($"/loans/{loanId}/return");

        var secondLoan = await CreateLoanAsync(book.Id, reader.Id);

        await Gateway.PostAsync<DataOutput<LoanCommandOutput>>(
            $"/loans/{secondLoan.Body!.Data!.Id}/cancel", new { reason = "Audited cancellation" });

        await Gateway.DeleteAsync<DataOutput<DeactivateBookCommandOutput>>($"/books/{book.Id}");

        // When - the trail is read back
        var events = await Gateway.GetAsync<PaginatedOutput<AuditEventOutput>>("/audit-events?pageSize=200");

        var actions = events.Body!.Data!
            .Where(entry => entry.EntityId == book.Id || entry.EntityId == reader.Id ||
                            entry.EntityId == loanId || entry.EntityId == secondLoan.Body.Data!.Id)
            .Select(entry => entry.Action)
            .ToList();

        // Then - all seven of the actions the challenge lists as a minimum
        Assert.Contains(AuditActions.BookCreated, actions);
        Assert.Contains(AuditActions.BookUpdated, actions);
        Assert.Contains(AuditActions.BookDeactivated, actions);
        Assert.Contains(AuditActions.UserCreated, actions);
        Assert.Contains(AuditActions.LoanCreated, actions);
        Assert.Contains(AuditActions.LoanReturned, actions);
        Assert.Contains(AuditActions.LoanCancelled, actions);
    }

    [FunctionalFact]
    public async Task GivenAnActorHeader_WhenCreatingABook_ThenTheEntryRecordsEverythingItMust()
    {
        // Given
        var correlationId = $"audit-{Guid.NewGuid():N}";
        var before = DateTime.UtcNow.AddSeconds(-5);

        var response = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "Complete Entry", isbn = UniqueIsbn(), author = "Author", totalCopies = 1
        }, new Dictionary<string, string>
        {
            [ActorHeader] = "librarian@example.com",
            [CorrelationHeader] = correlationId
        });

        var book = response.Body!.Data!;

        // When
        await using var context = Containers.CreateContext();

        var entry = await context.AuditEvents.FirstAsync(
            audit => audit.EntityId == book.Id && audit.Action == AuditActions.BookCreated);

        // Then - entity, identifier, action, actor, UTC timestamp, correlation identifier
        Assert.Equal(AuditEntityTypes.Book, entry.EntityType);
        Assert.Equal(book.Id, entry.EntityId);
        Assert.Equal("librarian@example.com", entry.Actor);
        Assert.Equal(correlationId, entry.CorrelationId);
        Assert.InRange(entry.OccurredAt, before, DateTime.UtcNow.AddSeconds(5));

        // Then - and enough of the change to understand it without reading the book row. The property
        // names are camel-cased, because the writer serializes with the web defaults - the same shape
        // the API's own responses use, so one convention covers both.
        Assert.Contains("Complete Entry", entry.Data);
        Assert.Contains("totalCopies", entry.Data);
    }

    [FunctionalFact]
    public async Task GivenNoActorHeader_WhenWriting_ThenTheTrailSaysAnonymousRatherThanGuessing()
    {
        // Given a request that names nobody
        var response = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "Anonymous Write", isbn = UniqueIsbn(), author = "Author", totalCopies = 1
        });

        var book = response.Body!.Data!;

        // When
        await using var context = Containers.CreateContext();

        var entry = await context.AuditEvents.FirstAsync(audit => audit.EntityId == book.Id);

        // Then - the trail is honest about what it knows
        Assert.Equal("anonymous", entry.Actor);
    }

    [FunctionalFact]
    public async Task GivenOneRequest_WhenFilteringByItsCorrelationId_ThenItsEntriesComeBack()
    {
        // Given
        var correlationId = $"audit-{Guid.NewGuid():N}";

        var response = await Gateway.PostAsync<DataOutput<BookCommandOutput>>("/books", new
        {
            title = "Correlated", isbn = UniqueIsbn(), author = "Author", totalCopies = 1
        }, new Dictionary<string, string> { [CorrelationHeader] = correlationId });

        // When
        var events = await Gateway.GetAsync<PaginatedOutput<AuditEventOutput>>(
            $"/audit-events?correlationId={correlationId}");

        // Then - the filter that turns the trail from a list into an explanation
        var entry = Assert.Single(events.Body!.Data!);

        Assert.Equal(response.Body!.Data!.Id, entry.EntityId);
        Assert.Equal(correlationId, entry.CorrelationId);
    }

    [FunctionalFact]
    public async Task GivenNoCorrelationHeader_WhenCallingAnyEndpoint_ThenOneIsGeneratedAndReturned()
    {
        // When
        var response = await Gateway.GetAsync<PaginatedOutput<BookOutput>>("/books");

        // Then - a caller can quote the identifier from any response, including one they did not
        // correlate themselves
        Assert.True(response.Headers.ContainsKey(CorrelationHeader));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers[CorrelationHeader]));
    }

    [FunctionalFact]
    public async Task GivenAnEntityFilter_WhenReadingTheTrail_ThenOnlyThatEntitysEntriesComeBack()
    {
        // Given
        var book = await CreateBookAsync(totalCopies: 1);

        // When
        var events = await Gateway.GetAsync<PaginatedOutput<AuditEventOutput>>(
            $"/audit-events?entityType=book&entityId={book.Id}");

        // Then
        Assert.NotEmpty(events.Body!.Data!);
        Assert.All(events.Body.Data, entry =>
        {
            Assert.Equal(AuditEntityTypes.Book, entry.EntityType);
            Assert.Equal(book.Id, entry.EntityId);
        });
    }

    [FunctionalFact]
    public async Task GivenAnUnknownEntityType_WhenReadingTheTrail_ThenItIsRefused()
    {
        // When
        var response = await Gateway.GetAsync<PaginatedOutput<AuditEventOutput>>(
            "/audit-events?entityType=spaceship");

        // Then
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [FunctionalFact]
    public async Task GivenARefusedLoan_WhenReadingTheTrail_ThenNoLoanEntryWasWritten()
    {
        // Given a book with nothing on the shelf
        var book = await CreateBookAsync(totalCopies: 1);
        var firstReader = await CreateUserAsync();
        var secondReader = await CreateUserAsync();

        await CreateLoanAsync(book.Id, firstReader.Id);

        var refused = await CreateLoanAsync(book.Id, secondReader.Id);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // When
        await using var context = Containers.CreateContext();

        var loanEntries = await context.AuditEvents.CountAsync(
            entry => entry.Action == AuditActions.LoanCreated &&
                     context.Loans.Any(loan =>
                         loan.PublicId == entry.EntityId && loan.Book.PublicId == book.Id));

        // Then - one loan happened, so the trail records one. A refusal that changed no state is a log
        // line, not a business event.
        Assert.Equal(1, loanEntries);
    }
}
