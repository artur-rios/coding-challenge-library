using System.Net;

namespace Library.Testing.Http;

/// <summary>The status, headers and deserialized body of one HTTP response.</summary>
/// <typeparam name="TBody">The expected body type.</typeparam>
public class HttpOutput<TBody>
{
    /// <summary>The status code.</summary>
    public HttpStatusCode StatusCode { get; init; }

    /// <summary>Whether the status is in the 2xx range.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>The deserialized body, or <c>default</c> when it was empty or would not parse.</summary>
    public TBody? Body { get; init; }

    /// <summary>
    ///     The raw body text.
    /// </summary>
    /// <remarks>
    ///     Kept alongside the parsed body so a failing assertion can show what actually came back -
    ///     without it, a response whose shape does not match <typeparamref name="TBody" /> fails with
    ///     "expected 201 but got 400" and no way to see why.
    /// </remarks>
    public string RawBody { get; init; } = string.Empty;

    /// <summary>The response headers, for the correlation identifier the API echoes.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();
}
