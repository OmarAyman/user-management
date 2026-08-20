using System.Net;
using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Localization;

/// <summary>
/// Localization end to end: the sentence changes with the request culture, the machine-readable code never
/// does.
/// </summary>
/// <remarks>
/// The failed sign-ins here use throwaway usernames rather than a demo account. Five wrong passwords lock an
/// account, and these tests share a database with every other test - pointing them at jdoe locked it for the
/// whole suite, which is how this note came to be written.
/// </remarks>
[Collection(ApiCollection.Name)]
public sealed class LocalizedErrorTests(ApiFixture fixture)
{
    private static readonly Uri LoginUri = new("/api/auth/login", UriKind.Relative);

    [Fact]
    public async Task An_authentication_failure_is_arabic_under_accept_language_ar()
    {
        using var client = fixture.Api.CreateCookieClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "ar");

        var response = await client.PostAsJsonAsync(
            LoginUri,
            new { username = TestData.Username("nobody"), password = "WrongPassword@1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.ReadProblemAsync();

        // The code is the contract and is never translated: the SPA and the tests branch on it.
        Assert.Equal(ErrorCodes.InvalidCredentials, problem.GetProperty("errorCode").GetString());

        // The sentence is.
        var title = problem.GetProperty("title").GetString();
        Assert.NotNull(title);
        Assert.True(ContainsArabic(title), $"Expected Arabic, got: {title}");
    }

    [Fact]
    public async Task The_same_failure_is_english_by_default()
    {
        using var client = fixture.Api.CreateCookieClient();

        var response = await client.PostAsJsonAsync(
            LoginUri,
            new { username = TestData.Username("nobody"), password = "WrongPassword@1" });

        var problem = await response.ReadProblemAsync();

        Assert.Equal(ErrorCodes.InvalidCredentials, problem.GetProperty("errorCode").GetString());
        Assert.Equal("Sign-in failed.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_culture_query_parameter_overrides_the_header_for_manual_testing()
    {
        using var client = fixture.Api.CreateCookieClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "en");

        var response = await client.PostAsJsonAsync(
            new Uri("/api/auth/login?culture=ar", UriKind.Relative),
            new { username = TestData.Username("nobody"), password = "WrongPassword@1" });

        var problem = await response.ReadProblemAsync();
        var title = problem.GetProperty("title").GetString();

        Assert.NotNull(title);
        Assert.True(ContainsArabic(title), $"Expected Arabic, got: {title}");
    }

    [Fact]
    public async Task An_unsupported_language_falls_back_to_english_rather_than_failing()
    {
        using var client = fixture.Api.CreateCookieClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "fr-CA,fr;q=0.9");

        var response = await client.PostAsJsonAsync(
            LoginUri,
            new { username = TestData.Username("nobody"), password = "WrongPassword@1" });

        // A browser sending a language this API does not speak is not an error.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.ReadProblemAsync();
        Assert.Equal("Sign-in failed.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Validation_messages_are_localized_field_by_field()
    {
        using var admin = fixture.Api.CreateCookieClient();
        admin.DefaultRequestHeaders.Add("Accept-Language", "ar");
        await admin.AuthenticateAsAdminAsync();

        var response = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username = "bad username!",
            email = $"{TestData.Username("localized")}@example.com",
            firstName = "A",
            lastName = "B",

            // Fails the uppercase and digit rules, which are custom messages resolved from the resource file.
            password = "alllowercaseletters",
            roleId = RoleIds.User,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.ReadProblemAsync();
        Assert.Equal(ErrorCodes.ValidationError, problem.GetProperty("errorCode").GetString());

        var errors = problem.GetProperty("errors");

        var usernameMessage = errors.GetProperty("username")[0].GetString();
        Assert.NotNull(usernameMessage);
        Assert.True(ContainsArabic(usernameMessage), $"Expected Arabic, got: {usernameMessage}");

        var passwordMessages = errors.GetProperty("password").EnumerateArray()
            .Select(message => message.GetString())
            .ToList();
        Assert.Contains(passwordMessages, message => message is not null && ContainsArabic(message));
    }

    [Fact]
    public async Task A_use_case_field_error_is_localized_even_though_the_handler_cannot_render_it()
    {
        using var admin = fixture.Api.CreateCookieClient();
        admin.DefaultRequestHeaders.Add("Accept-Language", "ar");
        await admin.AuthenticateAsAdminAsync();

        // The handler names a resource key for this failure; the API renders it in the request's culture.
        var response = await admin.GetAsync(new Uri("/api/users?roleId=99", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.ReadProblemAsync();
        var message = problem.GetProperty("errors").GetProperty("roleId")[0].GetString();

        Assert.NotNull(message);
        Assert.True(ContainsArabic(message), $"Expected Arabic, got: {message}");

        // The interpolated argument still arrives: a localized message is not a message without data.
        Assert.Contains("99", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_conflict_is_localized_while_keeping_its_code()
    {
        using var admin = fixture.Api.CreateCookieClient();
        await admin.AuthenticateAsAdminAsync();

        var username = TestData.Username("localizedconflict");

        var create = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username,
            email = $"{username}@example.com",
            firstName = "First",
            lastName = "User",
            password = "Created@123456",
            roleId = RoleIds.User,
        });
        create.EnsureSuccessStatusCode();

        admin.DefaultRequestHeaders.Add("Accept-Language", "ar");

        var duplicate = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username,
            email = $"{TestData.Username("other")}@example.com",
            firstName = "Second",
            lastName = "User",
            password = "Created@123456",
            roleId = RoleIds.User,
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var problem = await duplicate.ReadProblemAsync();
        Assert.Equal(ErrorCodes.UsernameAlreadyExists, problem.GetProperty("errorCode").GetString());

        var detail = problem.GetProperty("detail").GetString();
        Assert.NotNull(detail);
        Assert.True(ContainsArabic(detail), $"Expected Arabic, got: {detail}");
    }

    [Fact]
    public async Task The_response_reports_the_culture_it_used()
    {
        using var client = fixture.Api.CreateCookieClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "ar");

        var response = await client.GetAsync(new Uri("/health", UriKind.Relative));

        // Lets a client - or a reviewer - confirm which culture was negotiated without guessing from the text.
        // Content-Language is a content header, not a response header - reading it from the wrong collection
        // throws rather than returning empty.
        Assert.Contains("ar", response.Content.Headers.GetValues("Content-Language"));
    }

    /// <summary>
    /// True when the text contains a character from the Arabic Unicode block.
    /// </summary>
    /// <remarks>
    /// Escapes rather than literal Arabic characters: the range is unambiguous in source regardless of the
    /// encoding a tool writes the file with, which a literal is not.
    /// </remarks>
    private static bool ContainsArabic(string value) =>
        value.Any(character => character is >= '\u0600' and <= '\u06FF');
}

