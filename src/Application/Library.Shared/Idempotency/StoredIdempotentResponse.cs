namespace Library.Shared.Idempotency;

/// <summary>What one <c>Idempotency-Key</c> produced the first time it was used.</summary>
/// <param name="Key">The caller-supplied key.</param>
/// <param name="Endpoint">The endpoint it was used against.</param>
/// <param name="RequestFingerprint">SHA-256 of the request payload it first arrived with.</param>
/// <param name="StatusCode">The HTTP status the first attempt answered with.</param>
/// <param name="ResponseBody">The response body the first attempt produced, as JSON.</param>
public sealed record StoredIdempotentResponse(
    string Key,
    string Endpoint,
    string RequestFingerprint,
    int StatusCode,
    string ResponseBody);
