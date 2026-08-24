using Library.Shared.Idempotency;
using Library.Testing.Attributes;

namespace Library.Domain.Tests;

// Unit tests for the idempotency fingerprint - the value that decides whether a repeated key is the
// same request or a different one.
public class IdempotencyFingerprintTests
{
    private static object Payload(Guid bookId, Guid userId, int loanDays) => new { bookId, userId, loanDays };

    [UnitFact]
    public void GivenIdenticalPayloads_WhenFingerprinting_ThenTheFingerprintsMatch()
    {
        // Given
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // When
        var first = IdempotencyFingerprint.Of(Payload(bookId, userId, 14));
        var second = IdempotencyFingerprint.Of(Payload(bookId, userId, 14));

        // Then - the property a retry depends on: the same request must fingerprint the same, or an
        // honest retry would be refused as a reuse
        Assert.Equal(first, second);
    }

    [UnitFact]
    public void GivenADifferentBook_WhenFingerprinting_ThenTheFingerprintChanges()
    {
        // Given
        var userId = Guid.NewGuid();

        // When
        var first = IdempotencyFingerprint.Of(Payload(Guid.NewGuid(), userId, 14));
        var second = IdempotencyFingerprint.Of(Payload(Guid.NewGuid(), userId, 14));

        // Then - the other half: a key reused for a different request must be detectable
        Assert.NotEqual(first, second);
    }

    [UnitFact]
    public void GivenADifferentLoanPeriod_WhenFingerprinting_ThenTheFingerprintChanges()
    {
        // Given
        var bookId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // When
        var first = IdempotencyFingerprint.Of(Payload(bookId, userId, 14));
        var second = IdempotencyFingerprint.Of(Payload(bookId, userId, 21));

        // Then - every field that changes what the API would do is part of the fingerprint
        Assert.NotEqual(first, second);
    }

    [UnitFact]
    public void GivenAnyPayload_WhenFingerprinting_ThenTheResultIsLowerCaseHexOfFixedLength()
    {
        // When
        var fingerprint = IdempotencyFingerprint.Of(Payload(Guid.NewGuid(), Guid.NewGuid(), 14));

        // Then - SHA-256 as 64 hex characters, which is what the column is sized for
        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }
}
