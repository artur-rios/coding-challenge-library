using Library.Shared.Catalog;
using Library.Testing.Attributes;

namespace Library.Domain.Tests;

// Unit tests for ISBN normalisation and checksum validation. These matter more than their size
// suggests: normalisation is what makes the unique index enforce "this book is already in the
// catalogue" rather than "this exact string is", and the checksum is what stops a mistyped digit
// becoming a permanent second entry for the same title.
public class IsbnTests
{
    [UnitTheory]
    [InlineData("978-0-306-40615-7", "9780306406157")]
    [InlineData("978 0 306 40615 7", "9780306406157")]
    [InlineData("  9780306406157  ", "9780306406157")]
    [InlineData("0-306-40615-2", "0306406152")]
    [InlineData("043942089x", "043942089X")]
    public void GivenPunctuatedOrCasedIsbn_WhenNormalising_ThenSeparatorsGoAndCheckDigitIsUpperCased(
        string input, string expected)
    {
        // When
        var normalized = Isbn.Normalize(input);

        // Then
        Assert.Equal(expected, normalized);
    }

    [UnitFact]
    public void GivenNull_WhenNormalising_ThenTheResultIsEmpty()
    {
        // A null ISBN reaches this from a command whose validator has not run yet, so it must not
        // throw - the validator is what reports it, with a message the caller can act on.
        Assert.Equal(string.Empty, Isbn.Normalize(null));
    }

    [UnitTheory]
    [InlineData("9780306406157")] // ISBN-13
    [InlineData("978-0-306-40615-7")]
    [InlineData("0306406152")] // ISBN-10
    [InlineData("043942089X")] // ISBN-10 whose check character is X, standing for ten
    [InlineData("9780132350884")]
    public void GivenWellFormedIsbn_WhenValidating_ThenItIsAccepted(string isbn)
    {
        Assert.True(Isbn.IsValid(isbn));
    }

    [UnitTheory]
    [InlineData("9780306406158")] // last digit wrong
    [InlineData("9770306406157")] // a digit in the middle wrong
    [InlineData("0306406153")] // ISBN-10 check digit wrong
    [InlineData("043942089Y")] // Y is not a check character
    [InlineData("978030640615X")] // X is only valid in ISBN-10's last position
    [InlineData("978030640615")] // twelve digits
    [InlineData("12345")]
    [InlineData("")]
    [InlineData(null)]
    public void GivenMalformedIsbn_WhenValidating_ThenItIsRefused(string? isbn)
    {
        Assert.False(Isbn.IsValid(isbn));
    }

    [UnitFact]
    public void GivenTwoSpellingsOfOneIsbn_WhenNormalising_ThenTheyBecomeTheSameValue()
    {
        // The property the unique index depends on, stated directly.
        Assert.Equal(Isbn.Normalize("978-0-306-40615-7"), Isbn.Normalize("9780306406157"));
    }
}
