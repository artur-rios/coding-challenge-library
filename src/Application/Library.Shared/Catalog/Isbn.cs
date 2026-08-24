namespace Library.Shared.Catalog;

/// <summary>
///     Normalisation and checksum validation for ISBNs, so uniqueness means what a librarian expects
///     it to mean.
/// </summary>
/// <remarks>
///     Without normalisation, <c>978-0-306-40615-7</c> and <c>9780306406157</c> are two different
///     strings, and a unique index would happily accept both as separate books. Storing the
///     normalised form is what makes the database constraint enforce the actual rule rather than a
///     rule about punctuation.
/// </remarks>
public static class Isbn
{
    /// <summary>
    ///     Strips separators and upper-cases the check character, yielding the form that is stored and
    ///     compared.
    /// </summary>
    public static string Normalize(string? value) =>
        new((value ?? string.Empty)
            .Where(character => !char.IsWhiteSpace(character) && character != '-')
            .Select(char.ToUpperInvariant)
            .ToArray());

    /// <summary>
    ///     Whether the normalised value is a well-formed ISBN-10 or ISBN-13, checksum included.
    /// </summary>
    /// <remarks>
    ///     The checksum is verified rather than only the length, because a mistyped digit produces a
    ///     string of the right shape that names a different book - and once it is in the catalogue
    ///     under a unique index, correcting it is somebody's afternoon.
    /// </remarks>
    public static bool IsValid(string? value)
    {
        var normalized = Normalize(value);

        return normalized.Length switch
        {
            10 => IsValidIsbn10(normalized),
            13 => IsValidIsbn13(normalized),
            _ => false
        };
    }

    // ISBN-10: the weighted sum of the digits, weights 10 down to 1, must be divisible by 11. The
    // final position may be 'X', standing for 10 - which is why the check character is upper-cased
    // during normalisation rather than compared case-insensitively here.
    private static bool IsValidIsbn10(string value)
    {
        var sum = 0;

        for (var position = 0; position < 10; position++)
        {
            var character = value[position];

            int digit;

            if (char.IsAsciiDigit(character))
            {
                digit = character - '0';
            }
            else if (character == 'X' && position == 9)
            {
                digit = 10;
            }
            else
            {
                return false;
            }

            sum += digit * (10 - position);
        }

        return sum % 11 == 0;
    }

    // ISBN-13: digits weighted 1 and 3 alternately must sum to a multiple of 10.
    private static bool IsValidIsbn13(string value)
    {
        if (!value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;

        for (var position = 0; position < 13; position++)
        {
            sum += (value[position] - '0') * (position % 2 == 0 ? 1 : 3);
        }

        return sum % 10 == 0;
    }
}
