using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Library.Testing.Http;

/// <summary>
///     A thin wrapper over <see cref="HttpClient" /> that returns status, headers and a deserialized
///     body together, so a functional test asserts on one object instead of juggling three.
/// </summary>
/// <param name="client">The client to issue requests with.</param>
public class HttpGateway(HttpClient client)
{
    /// <summary>The underlying client, for the occasional request that needs a custom header.</summary>
    public HttpClient Client => client;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Matches the API's own configuration, so a LoanStatus arrives back as the enum rather than
        // failing to parse "Active" into a number.
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Sends a GET and deserializes the response.</summary>
    public Task<HttpOutput<TBody>> GetAsync<TBody>(string route, CancellationToken ct = default) =>
        SendAsync<TBody>(new HttpRequestMessage(HttpMethod.Get, route), ct);

    /// <summary>Sends a POST with an optional JSON payload.</summary>
    public Task<HttpOutput<TBody>> PostAsync<TBody>(
        string route,
        object? payload = null,
        IDictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = Serialize(payload) };

        AddHeaders(request, headers);

        return SendAsync<TBody>(request, ct);
    }

    /// <summary>Sends a PATCH with an optional JSON payload.</summary>
    public Task<HttpOutput<TBody>> PatchAsync<TBody>(
        string route, object? payload = null, CancellationToken ct = default) =>
        SendAsync<TBody>(
            new HttpRequestMessage(HttpMethod.Patch, route) { Content = Serialize(payload) }, ct);

    /// <summary>Sends a DELETE.</summary>
    public Task<HttpOutput<TBody>> DeleteAsync<TBody>(string route, CancellationToken ct = default) =>
        SendAsync<TBody>(new HttpRequestMessage(HttpMethod.Delete, route), ct);

    private static HttpContent? Serialize(object? payload) =>
        payload is null
            ? null
            : new StringContent(
                JsonSerializer.Serialize(payload, SerializerOptions), Encoding.UTF8, "application/json");

    private static void AddHeaders(HttpRequestMessage request, IDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private async Task<HttpOutput<TBody>> SendAsync<TBody>(
        HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await client.SendAsync(request, ct);

        var raw = await response.Content.ReadAsStringAsync(ct);

        TBody? body = default;

        try
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                body = JsonSerializer.Deserialize<TBody>(raw, SerializerOptions);
            }
        }
        catch (JsonException)
        {
            // A refusal comes back as problem details rather than as the success envelope the caller
            // named, so this is the ordinary path for a negative test. RawBody carries the content
            // either way, and the status is what those tests assert on.
        }

        return new HttpOutput<TBody>
        {
            StatusCode = response.StatusCode,
            IsSuccess = response.IsSuccessStatusCode,
            Body = body,
            RawBody = raw,
            Headers = response.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase)
        };
    }
}
