using Library.Command.Input;
using Library.Command.Tests.Support;
using Library.Domain.Enums;
using Library.Shared.Caching;
using Library.Shared.Messages;
using Library.Testing.Attributes;

namespace Library.Command.Tests;

// Unit tests for the catalogue write handlers: creation, the PATCH semantics of the update, the copy
// count's atomic adjustment, and deactivation's refusal to erase history.
public class BookCommandHandlerTests
{
    private static CreateBookCommand ValidCreate(string isbn = "9780306406157") => new()
    {
        Title = "The Pragmatic Programmer",
        Isbn = isbn,
        Author = "Hunt and Thomas",
        TotalCopies = 3
    };

    [UnitFact]
    public async Task GivenValidRequest_WhenCreatingBook_ThenBookIsCreatedWithEveryCopyOnTheShelf()
    {
        // Given
        var harness = new CatalogHandlerHarness();

        // When
        var output = await harness.CreateBookHandler().HandleAsync(ValidCreate());

        // Then
        Assert.True(output.Success);
        Assert.Equal(3, output.Data!.TotalCopies);
        Assert.Equal(3, output.Data.AvailableCopies);
        Assert.True(output.Data.IsActive);
        Assert.Contains(BookMessages.BookCreatedSuccessfully, output.Messages);
    }

    [UnitTheory]
    [InlineData("978-0-306-40615-7")]
    [InlineData("978 0 306 40615 7")]
    [InlineData("0306406152")]
    public async Task GivenPunctuatedIsbn_WhenCreatingBook_ThenItIsStoredNormalised(string isbn)
    {
        // Given
        var harness = new CatalogHandlerHarness();

        // When
        var output = await harness.CreateBookHandler().HandleAsync(ValidCreate(isbn));

        // Then - separators are gone, so the unique index compares books rather than strings
        Assert.True(output.Success);
        Assert.DoesNotContain('-', output.Data!.Isbn);
        Assert.DoesNotContain(' ', output.Data.Isbn);
    }

    [UnitTheory]
    [InlineData("9780306406158")] // ISBN-13 with a broken checksum
    [InlineData("0306406153")] // ISBN-10 with a broken checksum
    [InlineData("12345")]
    [InlineData("not-an-isbn")]
    public async Task GivenInvalidIsbn_WhenCreatingBook_ThenItIsRefused(string isbn)
    {
        // Given
        var harness = new CatalogHandlerHarness();

        // When
        var output = await harness.CreateBookHandler().HandleAsync(ValidCreate(isbn));

        // Then - the checksum is verified, not only the length: a mistyped digit names a different
        // book, and correcting it once it is under a unique index is somebody's afternoon
        Assert.False(output.Success);
        Assert.Contains(BookMessages.IsbnInvalid, output.Errors);
    }

    [UnitFact]
    public async Task GivenDuplicateIsbn_WhenCreatingBook_ThenTheConflictIsReportedInDomainTerms()
    {
        // Given a repository that will report the unique-index violation the database would raise
        var harness = new CatalogHandlerHarness();
        harness.Books.FailNextWriteWithUniqueViolation();

        // When
        var output = await harness.CreateBookHandler().HandleAsync(ValidCreate());

        // Then - translated into a message about ISBNs rather than left as a generic conflict
        Assert.False(output.Success);
        Assert.Contains(BookMessages.IsbnAlreadyRegistered, output.Errors);
    }

    [UnitFact]
    public async Task GivenCreatedBook_WhenCreatingBook_ThenCatalogCacheIsDroppedAndTrailIsWritten()
    {
        // Given
        var harness = new CatalogHandlerHarness();

        // When
        var output = await harness.CreateBookHandler().HandleAsync(ValidCreate());

        // Then
        Assert.Contains(CacheKeys.CatalogPrefix, harness.Cache.RemovedKeys);

        var entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditEntityTypes.Book, entry.EntityType);
        Assert.Equal(AuditActions.BookCreated, entry.Action);
        Assert.Equal(output.Data!.Id, entry.EntityId);
    }

    [UnitFact]
    public async Task GivenOnlyTitleSupplied_WhenUpdatingBook_ThenOtherFieldsAreUntouched()
    {
        // Given
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync();

        // When
        var output = await harness.UpdateBookHandler()
            .HandleAsync(new UpdateBookCommand { Id = book.PublicId, Title = "A New Title" });

        // Then - PATCH semantics: an omitted field means "leave it alone", never "blank it"
        Assert.True(output.Success);
        Assert.Equal("A New Title", output.Data!.Title);
        Assert.Equal("Hunt and Thomas", output.Data.Author);
        Assert.Equal(3, output.Data.TotalCopies);
    }

    [UnitFact]
    public async Task GivenNoFieldsSupplied_WhenUpdatingBook_ThenTheRequestIsRefused()
    {
        // Given
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync();

        // When
        var output = await harness.UpdateBookHandler()
            .HandleAsync(new UpdateBookCommand { Id = book.PublicId });

        // Then - almost always a client that serialized the wrong object; answering 200 would hide it
        Assert.False(output.Success);
        Assert.Contains(BookMessages.NoFieldsToUpdate, output.Errors);
    }

    [UnitFact]
    public async Task GivenIncreasedTotal_WhenUpdatingBook_ThenAvailableCopiesMoveByTheSameDelta()
    {
        // Given a book with one copy out on loan
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 3, availableCopies: 2);

        // When two more copies are acquired
        var output = await harness.UpdateBookHandler()
            .HandleAsync(new UpdateBookCommand { Id = book.PublicId, TotalCopies = 5 });

        // Then - the copy still out stays out; the shelf gains exactly the two that were added
        Assert.True(output.Success);
        Assert.Equal(5, output.Data!.TotalCopies);
        Assert.Equal(4, output.Data.AvailableCopies);
    }

    [UnitFact]
    public async Task GivenTotalBelowCopiesOnLoan_WhenUpdatingBook_ThenChangeIsRefusedAndNothingIsWritten()
    {
        // Given a book with two of three copies out
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 3, availableCopies: 1);

        // When somebody tries to shrink the catalogue below what readers are holding
        var output = await harness.UpdateBookHandler()
            .HandleAsync(new UpdateBookCommand
            {
                Id = book.PublicId, Title = "Renamed", TotalCopies = 1
            });

        // Then - refused, and the title change goes back with it rather than being left half-applied
        Assert.False(output.Success);
        Assert.Contains(BookMessages.TotalCopiesBelowActiveLoans, output.Errors);
        Assert.False(harness.UnitOfWork.Committed);
        Assert.Equal(3, book.TotalCopies);
    }

    [UnitFact]
    public async Task GivenUpdatedBook_WhenUpdatingBook_ThenAuditEntryCarriesBeforeAndAfter()
    {
        // Given
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync();

        // When
        await harness.UpdateBookHandler()
            .HandleAsync(new UpdateBookCommand { Id = book.PublicId, Title = "A New Title" });

        // Then - enough to understand the change without finding the row, which is what the trail is for
        var entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditActions.BookUpdated, entry.Action);

        var before = entry.Data.GetType().GetProperty("Before")!.GetValue(entry.Data)!;
        var after = entry.Data.GetType().GetProperty("After")!.GetValue(entry.Data)!;

        Assert.Equal("The Pragmatic Programmer", before.GetType().GetProperty("Title")!.GetValue(before));
        Assert.Equal("A New Title", after.GetType().GetProperty("Title")!.GetValue(after));
    }

    [UnitFact]
    public async Task GivenUnknownBook_WhenUpdatingBook_ThenNotFoundIsReported()
    {
        // Given
        var harness = new CatalogHandlerHarness();

        // When
        var output = await harness.UpdateBookHandler()
            .HandleAsync(new UpdateBookCommand { Id = Guid.NewGuid(), Title = "Anything" });

        // Then
        Assert.False(output.Success);
        Assert.Contains(BookMessages.BookNotFound, output.Errors);
    }

    [UnitFact]
    public async Task GivenBookWithReturnedLoans_WhenDeactivating_ThenHistoryIsPreservedAndCounted()
    {
        // Given a book whose copies have all come back
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2);
        await harness.SeedLoanAsync(book, LoanStatus.Returned);
        await harness.SeedLoanAsync(book, LoanStatus.Cancelled);

        // When
        var output = await harness.DeactivateBookHandler()
            .HandleAsync(new DeactivateBookCommand { Id = book.PublicId });

        // Then - deactivated, not deleted, and the response says what was kept
        Assert.True(output.Success);
        Assert.False(output.Data!.IsActive);
        Assert.Equal(2, output.Data.PreservedLoanCount);
        Assert.Contains(BookMessages.BookDeactivatedSuccessfully, output.Messages);

        // Then - the loans are still there, which is the point of the whole endpoint
        Assert.Equal(2, harness.Loans.Items.Count);
        Assert.Single(harness.Books.Items);
    }

    [UnitFact]
    public async Task GivenBookWithCopiesOnLoan_WhenDeactivating_ThenItIsRefused()
    {
        // Given a book with a copy still out
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync(totalCopies: 2, availableCopies: 1);
        await harness.SeedLoanAsync(book);

        // When
        var output = await harness.DeactivateBookHandler()
            .HandleAsync(new DeactivateBookCommand { Id = book.PublicId });

        // Then - closing the title while readers hold copies would leave those loans pointing at a
        // catalogue entry that no longer accepts returns
        Assert.False(output.Success);
        Assert.Contains(BookMessages.BookHasActiveLoans, output.Errors);
        Assert.True(book.IsActive);
    }

    [UnitFact]
    public async Task GivenAlreadyInactiveBook_WhenDeactivating_ThenItIsAnsweredIdempotently()
    {
        // Given
        var harness = new CatalogHandlerHarness();
        var book = await harness.SeedBookAsync(isActive: false);

        // When
        var output = await harness.DeactivateBookHandler()
            .HandleAsync(new DeactivateBookCommand { Id = book.PublicId });

        // Then - a success with a distinct message, and nothing written: the caller has the outcome
        // they asked for and there is nothing for them to fix
        Assert.True(output.Success);
        Assert.True(output.Data!.AlreadyInactive);
        Assert.Contains(BookMessages.BookAlreadyInactive, output.Messages);
        Assert.Empty(harness.Audit.Entries);
    }

    [UnitFact]
    public async Task GivenValidRequest_WhenCreatingUser_ThenEmailIsStoredLowerCased()
    {
        // Given
        var harness = new CatalogHandlerHarness();

        // When
        var output = await harness.CreateUserHandler().HandleAsync(new CreateUserCommand
        {
            Name = " Ana Silva ", Email = "Ana.Silva@Example.COM"
        });

        // Then - normalised, so the unique index means "already registered" rather than "same casing"
        Assert.True(output.Success);
        Assert.Equal("ana.silva@example.com", output.Data!.Email);
        Assert.Equal("Ana Silva", output.Data.Name);
    }

    [UnitFact]
    public async Task GivenDuplicateEmail_WhenCreatingUser_ThenTheConflictIsReportedInDomainTerms()
    {
        // Given
        var harness = new CatalogHandlerHarness();
        harness.Users.FailNextWriteWithUniqueViolation();

        // When
        var output = await harness.CreateUserHandler().HandleAsync(new CreateUserCommand
        {
            Name = "Ana Silva", Email = "ana@example.com"
        });

        // Then
        Assert.False(output.Success);
        Assert.Contains(UserMessages.EmailAlreadyRegistered, output.Errors);
    }

    [UnitTheory]
    [InlineData("", "ana@example.com", UserMessages.NameRequired)]
    [InlineData("Ana", "", UserMessages.EmailRequired)]
    [InlineData("Ana", "not-an-email", UserMessages.EmailInvalid)]
    public async Task GivenInvalidInput_WhenCreatingUser_ThenItIsRefused(
        string name, string email, string expectedError)
    {
        // Given
        var harness = new CatalogHandlerHarness();

        // When
        var output = await harness.CreateUserHandler()
            .HandleAsync(new CreateUserCommand { Name = name, Email = email });

        // Then
        Assert.False(output.Success);
        Assert.Contains(expectedError, output.Errors);
    }
}
