using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace UserManagement.IntegrationTests.TestSupport;

/// <summary>Sign-in helpers, so a test that is about authorization is not half sign-in plumbing.</summary>
public static class ApiClientExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<LoginPayload> LoginAsync(this HttpClient client, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(client);

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username, password });

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<LoginPayload>(Json);

        return payload ?? throw new InvalidOperationException("Login returned no payload.");
    }

    /// <summary>Signs in and attaches the bearer token to every later request on this client.</summary>
    public static async Task<LoginPayload> AuthenticateAsync(
        this HttpClient client,
        string username,
        string password)
    {
        var payload = await client.LoginAsync(username, password);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.AccessToken);

        return payload;
    }

    public static Task<LoginPayload> AuthenticateAsAdminAsync(this HttpClient client) =>
        client.AuthenticateAsync(DemoCredentials.AdminUsername, DemoCredentials.AdminPassword);

    public static Task<LoginPayload> AuthenticateAsUserAsync(this HttpClient client) =>
        client.AuthenticateAsync(DemoCredentials.UserUsername, DemoCredentials.UserPassword);

    public static Task<LoginPayload> AuthenticateAsReadOnlyAsync(this HttpClient client) =>
        client.AuthenticateAsync(DemoCredentials.ReadOnlyUsername, DemoCredentials.ReadOnlyPassword);

    /// <summary>Reads the machine-readable error code out of a ProblemDetails response.</summary>
    public static async Task<string?> ReadErrorCodeAsync(this HttpResponseMessage response)
    {
        var problem = await response.ReadProblemAsync();

        return problem.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    /// <summary>
    /// Reads a ProblemDetails body as JSON.
    /// </summary>
    /// <remarks>
    /// Goes through the buffered string rather than <c>ReadFromJsonAsync</c>, which streams and then disposes
    /// the content: a test that checks the code and then inspects another member would otherwise fail on a
    /// closed stream, which looks like an API defect and is not one. Deserializing from the string also gives
    /// an element that owns its data, so it stays valid after this method returns.
    /// </remarks>
    public static async Task<JsonElement> ReadProblemAsync(this HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync();

        return string.IsNullOrWhiteSpace(body)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(body, Json);
    }
}

public sealed record LoginPayload(string AccessToken, DateTimeOffset ExpiresAt, LoginUserPayload User);

public sealed record LoginUserPayload(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    string Role);
