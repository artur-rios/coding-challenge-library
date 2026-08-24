using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Library.Shared.Idempotency;

/// <summary>
///     Reduces a request payload to a fixed-length fingerprint, so a repeat of a key can be checked
///     against the request it was first used with.
/// </summary>
public static class IdempotencyFingerprint
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Computes the lower-case hexadecimal SHA-256 of <paramref name="payload" />'s JSON form.</summary>
    /// <remarks>
    ///     Serialized here rather than hashing the raw request body, so the fingerprint depends on the
    ///     values the API actually acted on and not on incidental formatting - whitespace or property
    ///     order in the caller's JSON would otherwise make an identical retry look like a different
    ///     request and get refused.
    /// </remarks>
    public static string Of(object payload)
    {
        var json = JsonSerializer.Serialize(payload, payload.GetType(), SerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));

        return Convert.ToHexStringLower(hash);
    }
}
