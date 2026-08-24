namespace Library.Shared.Messages;

/// <summary>
///     Canonical messages produced by the catalogue use cases. Each is mapped to an HTTP status code
///     in <see cref="BookMessageMap" />.
/// </summary>
public static class BookMessages
{
    /// <summary>The book was added to the catalogue.</summary>
    public const string BookCreatedSuccessfully = "Book created successfully.";

    /// <summary>A single book was retrieved.</summary>
    public const string BookRetrievedSuccessfully = "Book retrieved successfully.";

    /// <summary>A page of books was retrieved.</summary>
    public const string BooksRetrievedSuccessfully = "Books retrieved successfully.";

    /// <summary>The book's details were updated.</summary>
    public const string BookUpdatedSuccessfully = "Book updated successfully.";

    /// <summary>The book was withdrawn from lending; its history was kept.</summary>
    public const string BookDeactivatedSuccessfully = "Book deactivated successfully. Its loan history is preserved.";

    /// <summary>The book was already inactive, so nothing was written.</summary>
    public const string BookAlreadyInactive = "Book is already inactive. Its loan history is preserved.";

    /// <summary>Availability for one book was retrieved.</summary>
    public const string AvailabilityRetrievedSuccessfully = "Availability retrieved successfully.";

    /// <summary>A book's loan history was retrieved.</summary>
    public const string HistoryRetrievedSuccessfully = "Loan history retrieved successfully.";

    /// <summary>No book holds that identifier.</summary>
    public const string BookNotFound = "Book not found.";

    /// <summary>Another book already carries the submitted ISBN.</summary>
    public const string IsbnAlreadyRegistered = "A book with this ISBN already exists.";

    /// <summary>
    ///     The book still has copies out on loan, so it cannot be withdrawn from the catalogue yet.
    /// </summary>
    public const string BookHasActiveLoans =
        "Book cannot be deactivated while copies are on loan. Return or cancel them first.";

    /// <summary>
    ///     The requested total would be smaller than the number of copies currently lent out, which
    ///     would leave the shelf owing copies it does not have.
    /// </summary>
    public const string TotalCopiesBelowActiveLoans =
        "Total copies cannot be fewer than the copies currently on loan.";

    /// <summary>The request changed nothing - every field was omitted.</summary>
    public const string NoFieldsToUpdate = "At least one field must be supplied.";

    /// <summary>The title was not supplied.</summary>
    public const string TitleRequired = "Title is required.";

    /// <summary>The title is longer than the column that stores it.</summary>
    public const string TitleTooLong = "Title must be at most 300 characters.";

    /// <summary>The author was not supplied.</summary>
    public const string AuthorRequired = "Author is required.";

    /// <summary>The author is longer than the column that stores it.</summary>
    public const string AuthorTooLong = "Author must be at most 200 characters.";

    /// <summary>The ISBN was not supplied.</summary>
    public const string IsbnRequired = "ISBN is required.";

    /// <summary>
    ///     The ISBN is not a well-formed ISBN-10 or ISBN-13, checksum included.
    /// </summary>
    public const string IsbnInvalid = "ISBN must be a valid ISBN-10 or ISBN-13.";

    /// <summary>A negative number of copies was submitted.</summary>
    public const string TotalCopiesNegative = "Total copies cannot be negative.";

    /// <summary>An implausibly large number of copies was submitted.</summary>
    public const string TotalCopiesTooLarge = "Total copies must be at most 100000.";
}
