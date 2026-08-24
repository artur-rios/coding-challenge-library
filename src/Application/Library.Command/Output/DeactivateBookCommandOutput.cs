using Library.Mediator.Command;

namespace Library.Command.Output;

/// <summary>What happened when a book was withdrawn from lending.</summary>
public class DeactivateBookCommandOutput : CommandOutput
{
    /// <summary>The book's public identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Whether the book is now active - always <c>false</c> on a successful call.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    ///     Whether the book was already inactive, so nothing was written. The response status is 200
    ///     either way; this is what tells a caller which of the two happened.
    /// </summary>
    public bool AlreadyInactive { get; set; }

    /// <summary>
    ///     How many loans of this book are on record. Reported because it is the reason the book was
    ///     deactivated rather than deleted: this history is what must not vanish.
    /// </summary>
    public int PreservedLoanCount { get; set; }
}
