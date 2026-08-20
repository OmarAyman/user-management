using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;
using UserManagement.IntegrationTests.Users;

namespace UserManagement.IntegrationTests.Audit;

/// <summary>
/// The audit trail as an administrator reads it, and the guarantees it has to keep.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuditEndpointTests(ApiFixture fixture)
{
    private static readonly Uri AuditUri = new("/api/audit-logs", UriKind.Relative);

    [Fact]
    public async Task A_full_lifecycle_produces_the_expected_actions_against_the_users_immutable_id()
    {
        using var admin = await AdminAsync();

        var created = await CreateAsync(admin, "auditlifecycle");

        var updated = await admin.PutAsJsonAsync(
            new Uri($"/api/users/{created.Id}", UriKind.Relative),
            new
            {
                email = created.Email,
                firstName = "Audited",
                lastName = created.LastName,
                roleId = RoleIds.ReadOnlyUser,
                rowVersion = created.RowVersion,
            });
        updated.EnsureSuccessStatusCode();

        (await admin.DeleteAsync(new Uri($"/api/users/{created.Id}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var reloaded = await admin.GetFromJsonAsync<UserDetailsPayload>(
            new Uri($"/api/users/{created.Id}", UriKind.Relative));
        Assert.NotNull(reloaded);

        (await admin.PostAsJsonAsync(
                new Uri($"/api/users/{created.Id}/restore", UriKind.Relative),
                new { rowVersion = reloaded.RowVersion }))
            .EnsureSuccessStatusCode();

        var page = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri($"/api/audit-logs?entityId={created.Id}&pageSize=50", UriKind.Relative));

        Assert.NotNull(page);

        var actions = page.Items.Select(entry => entry.Action).ToList();

        Assert.Contains("Insert", actions);
        Assert.Contains("Update", actions);
        Assert.Contains("Delete", actions);
        Assert.Contains("Restore", actions);

        // A role change writes its own row in addition to the update, so privilege movement is a
        // single-column filter rather than a JSON diff.
        Assert.Contains("RoleChange", actions);

        // Every row names the immutable id, which is what keeps the trail unambiguous once a released
        // username has been taken by somebody else (ADR-0009).
        Assert.All(page.Items, entry => Assert.Equal(created.Id.ToString(), entry.EntityId));

        // The username is carried as a readability snapshot, never as the key.
        Assert.All(page.Items, entry => Assert.Equal(created.Username, entry.EntityDisplayName));

        Assert.All(page.Items, entry => Assert.Equal(DemoCredentials.AdminUsername, entry.PerformedByUsername));
        Assert.All(page.Items, entry => Assert.False(string.IsNullOrWhiteSpace(entry.IpAddress)));
        Assert.All(page.Items, entry => Assert.False(string.IsNullOrWhiteSpace(entry.CorrelationId)));
    }

    [Fact]
    public async Task A_role_change_records_the_old_and_new_role_as_readable_json()
    {
        using var admin = await AdminAsync();
        var created = await CreateAsync(admin, "auditrole");

        (await admin.PutAsJsonAsync(
                new Uri($"/api/users/{created.Id}", UriKind.Relative),
                new
                {
                    email = created.Email,
                    firstName = created.FirstName,
                    lastName = created.LastName,
                    roleId = RoleIds.Admin,
                    rowVersion = created.RowVersion,
                }))
            .EnsureSuccessStatusCode();

        var page = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri($"/api/audit-logs?entityId={created.Id}&action=RoleChange", UriKind.Relative));

        Assert.NotNull(page);
        var entry = Assert.Single(page.Items);

        // Objects, not escaped strings: a UI can render a field-by-field diff without re-parsing.
        Assert.Equal(JsonValueKind.Object, entry.OldValues!.Value.ValueKind);
        Assert.Equal(RoleIds.User, entry.OldValues.Value.GetProperty("roleId").GetInt32());
        Assert.Equal(RoleIds.Admin, entry.NewValues!.Value.GetProperty("roleId").GetInt32());
    }

    [Fact]
    public async Task A_password_change_is_visible_without_exposing_any_password_material()
    {
        using var admin = await AdminAsync();

        var username = TestData.Username("auditpw");
        var created = await CreateAsync(admin, "auditpw", username);

        using var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsync(username, "Created@123456");

        (await client.PostAsJsonAsync(
                new Uri("/api/users/me/change-password", UriKind.Relative),
                new { currentPassword = "Created@123456", newPassword = "Replaced@123456" }))
            .EnsureSuccessStatusCode();

        var page = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri($"/api/audit-logs?entityId={created.Id}&pageSize=50", UriKind.Relative));

        Assert.NotNull(page);

        var passwordChange = page.Items.FirstOrDefault(entry =>
            entry.NewValues is not null && entry.NewValues.Value.TryGetProperty("passwordHash", out _));

        // That a password changed is exactly what an auditor needs to see...
        Assert.NotNull(passwordChange);
        Assert.Equal("***", passwordChange.NewValues!.Value.GetProperty("passwordHash").GetString());

        // ...and what it changed to is exactly what they must not.
        var body = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("AQAAAA", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Created@123456", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Replaced@123456", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_audit_row_ever_contains_token_material()
    {
        using var admin = await AdminAsync();

        // Sign-ins and refreshes create and rotate refresh tokens; none of it belongs in the audit trail.
        using var client = fixture.Api.CreateCookieClient();
        await client.LoginAsync(DemoCredentials.UserUsername, DemoCredentials.UserPassword);
        await client.PostAsync(new Uri("/api/auth/refresh", UriKind.Relative), null);

        var page = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri("/api/audit-logs?pageSize=100", UriKind.Relative));
        Assert.NotNull(page);

        var body = await admin.GetStringAsync(new Uri("/api/audit-logs?pageSize=100", UriKind.Relative));

        // RefreshToken is not an audited entity at all: rotation is mechanical and would flood the trail, so
        // token events are security log events instead (audit policy 2 and 6).
        Assert.DoesNotContain("tokenHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("familyId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eyJ", body, StringComparison.Ordinal);

        // SecurityStamp is redacted rather than excluded, which is deliberate: that a credential rotated is
        // exactly what an auditor needs to see, and the value is exactly what they must not. So the key may
        // appear - carrying nothing.
        foreach (var entry in page.Items)
        {
            foreach (var values in new[] { entry.OldValues, entry.NewValues })
            {
                if (values?.TryGetProperty("securityStamp", out var stamp) == true)
                {
                    Assert.Equal("***", stamp.GetString());
                }
            }
        }
    }

    [Fact]
    public async Task Excluded_columns_do_not_create_audit_rows()
    {
        using var admin = await AdminAsync();

        var before = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri("/api/audit-logs?pageSize=1", UriKind.Relative));
        Assert.NotNull(before);

        // A sign-in writes LastLoginAt and resets the failure counters. Those are excluded from auditing on
        // purpose - every sign-in would otherwise swamp the trail (audit policy 4.3).
        using var client = fixture.Api.CreateCookieClient();
        await client.LoginAsync(DemoCredentials.ReadOnlyUsername, DemoCredentials.ReadOnlyPassword);

        var after = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri("/api/audit-logs?pageSize=1", UriKind.Relative));
        Assert.NotNull(after);

        Assert.Equal(before.TotalCount, after.TotalCount);
    }

    [Fact]
    public async Task Audit_rows_are_unchanged_by_later_operations()
    {
        using var admin = await AdminAsync();
        var created = await CreateAsync(admin, "auditimmutable");

        var original = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri($"/api/audit-logs?entityId={created.Id}", UriKind.Relative));
        Assert.NotNull(original);
        var insertRow = Assert.Single(original.Items);

        (await admin.PutAsJsonAsync(
                new Uri($"/api/users/{created.Id}", UriKind.Relative),
                new
                {
                    email = created.Email,
                    firstName = "Changed",
                    lastName = created.LastName,
                    roleId = created.RoleId,
                    rowVersion = created.RowVersion,
                }))
            .EnsureSuccessStatusCode();

        var afterwards = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri($"/api/audit-logs?entityId={created.Id}&action=Insert", UriKind.Relative));
        Assert.NotNull(afterwards);
        var sameRow = Assert.Single(afterwards.Items);

        // Append-only: the earlier entry is byte-identical after a later change.
        Assert.Equal(insertRow.Id, sameRow.Id);
        Assert.Equal(insertRow.Timestamp, sameRow.Timestamp);
        Assert.Equal(
            insertRow.NewValues?.GetRawText(),
            sameRow.NewValues?.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Only_administrators_can_read_the_trail(bool readOnly)
    {
        using var client = fixture.Api.CreateCookieClient();

        if (readOnly)
        {
            await client.AuthenticateAsReadOnlyAsync();
        }
        else
        {
            await client.AuthenticateAsUserAsync();
        }

        var response = await client.GetAsync(AuditUri);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_inverted_date_range_is_rejected()
    {
        using var admin = await AdminAsync();

        var response = await admin.GetAsync(new Uri(
            "/api/audit-logs?fromUtc=2026-08-20T00:00:00Z&toUtc=2026-08-01T00:00:00Z",
            UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task An_unknown_sort_field_is_refused()
    {
        using var admin = await AdminAsync();

        var response = await admin.GetAsync(new Uri("/api/audit-logs?sortBy=oldValues", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidSortField, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task The_trail_is_newest_first_by_default()
    {
        using var admin = await AdminAsync();

        var page = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri("/api/audit-logs?pageSize=20", UriKind.Relative));

        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);

        var timestamps = page.Items.Select(entry => entry.Timestamp).ToList();
        Assert.Equal(timestamps.OrderByDescending(timestamp => timestamp), timestamps);
    }

    [Fact]
    public async Task There_is_no_route_that_modifies_an_audit_entry()
    {
        using var admin = await AdminAsync();

        var page = await admin.GetFromJsonAsync<AuditPagePayload>(
            new Uri("/api/audit-logs?pageSize=1", UriKind.Relative));
        Assert.NotNull(page);
        var entry = page.Items.Single();

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            var response = await admin.SendAsync(
                new HttpRequestMessage(method, $"/api/audit-logs/{entry.Id}"));

            // 404 or 405, never 2xx: the immutability of the trail is a property of the routing table, not of
            // a check somebody has to remember.
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{method} returned {response.StatusCode}");
        }
    }

    private async Task<HttpClient> AdminAsync()
    {
        var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsAdminAsync();

        return client;
    }

    private static async Task<UserDetailsPayload> CreateAsync(
        HttpClient admin,
        string prefix,
        string? username = null)
    {
        var name = username ?? TestData.Username(prefix);

        var response = await admin.PostAsJsonAsync(new Uri("/api/users", UriKind.Relative), new
        {
            username = name,
            email = $"{name}@example.com",
            firstName = "Audit",
            lastName = "Target",
            password = "Created@123456",
            roleId = RoleIds.User,
        });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDetailsPayload>()
               ?? throw new InvalidOperationException("Create returned no payload.");
    }
}

public sealed record AuditPagePayload(List<AuditEntryPayload> Items, int PageNumber, int PageSize, int TotalCount);

public sealed record AuditEntryPayload(
    long Id,
    string EntityName,
    string EntityId,
    string? EntityDisplayName,
    string Action,
    Guid? PerformedByUserId,
    string PerformedByUsername,
    DateTimeOffset Timestamp,
    string IpAddress,
    JsonElement? OldValues,
    JsonElement? NewValues,
    string? CorrelationId);

