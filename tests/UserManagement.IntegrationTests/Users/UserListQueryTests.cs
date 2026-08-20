using System.Net;
using System.Net.Http.Json;
using UserManagement.Domain.Constants;
using UserManagement.IntegrationTests.TestSupport;

namespace UserManagement.IntegrationTests.Users;

/// <summary>Search, role filter, sorting, paging - and the ways each of them can be asked for wrongly.</summary>
[Collection(ApiCollection.Name)]
public sealed class UserListQueryTests(ApiFixture fixture)
{
    [Fact]
    public async Task A_page_carries_items_and_honest_metadata()
    {
        using var client = await AdminAsync();

        var page = await client.GetFromJsonAsync<PagedPayload>(
            new Uri("/api/users?pageNumber=1&pageSize=5", UriKind.Relative));

        Assert.NotNull(page);
        Assert.True(page.Items.Count <= 5);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal(5, page.PageSize);
        Assert.True(page.TotalCount >= 3);
        Assert.Equal((int)Math.Ceiling(page.TotalCount / 5.0), page.TotalPages);
        Assert.False(page.HasPreviousPage);
    }

    [Fact]
    public async Task Paging_does_not_repeat_or_drop_rows_across_pages()
    {
        using var client = await AdminAsync();

        var first = await client.GetFromJsonAsync<PagedPayload>(
            new Uri("/api/users?pageNumber=1&pageSize=4&sortBy=createdAt&sortDirection=Descending", UriKind.Relative));
        var second = await client.GetFromJsonAsync<PagedPayload>(
            new Uri("/api/users?pageNumber=2&pageSize=4&sortBy=createdAt&sortDirection=Descending", UriKind.Relative));

        Assert.NotNull(first);
        Assert.NotNull(second);

        // The seeded users share a creation instant closely enough that without the Id tiebreaker in the ORDER
        // BY, a row can appear on both pages or on neither. That is the bug this asserts against.
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
    }

    [Fact]
    public async Task A_page_past_the_end_is_an_empty_page_not_an_error()
    {
        using var client = await AdminAsync();

        var response = await client.GetAsync(new Uri("/api/users?pageNumber=9999&pageSize=10", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedPayload>();
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.True(page.TotalCount > 0);
    }

    [Theory]
    [InlineData("pageNumber=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task Paging_outside_the_allowed_bounds_is_rejected(string query)
    {
        using var client = await AdminAsync();

        var response = await client.GetAsync(new Uri($"/api/users?{query}", UriKind.Relative));

        // Rejected rather than clamped: quietly serving a different page size than was asked for makes a client
        // believe it has the whole set.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task Search_matches_username_email_first_name_and_last_name()
    {
        using var admin = await AdminAsync();

        var marker = Guid.NewGuid().ToString("N")[..8];
        await CreateUserAsync(admin, $"searchable{marker}", firstName: "Zephyr", lastName: $"Quill{marker}");

        foreach (var term in new[] { $"searchable{marker}", "Zephyr", $"Quill{marker}", marker })
        {
            var page = await admin.GetFromJsonAsync<PagedPayload>(
                new Uri($"/api/users?search={Uri.EscapeDataString(term)}", UriKind.Relative));

            Assert.NotNull(page);
            Assert.Contains(page.Items, item => item.Username == $"searchable{marker}");
        }
    }

    [Fact]
    public async Task An_empty_search_behaves_as_no_search()
    {
        using var client = await AdminAsync();

        var withEmpty = await client.GetFromJsonAsync<PagedPayload>(
            new Uri("/api/users?search=", UriKind.Relative));
        var without = await client.GetFromJsonAsync<PagedPayload>(new Uri("/api/users", UriKind.Relative));

        Assert.NotNull(withEmpty);
        Assert.NotNull(without);
        Assert.Equal(without.TotalCount, withEmpty.TotalCount);
    }

    [Fact]
    public async Task The_role_filter_returns_only_that_role()
    {
        using var client = await AdminAsync();

        var page = await client.GetFromJsonAsync<PagedPayload>(
            new Uri($"/api/users?roleId={RoleIds.Admin}&pageSize=100", UriKind.Relative));

        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, item => Assert.Equal(RoleNames.Admin, item.Role));
    }

    [Fact]
    public async Task Filtering_by_a_role_that_does_not_exist_is_a_client_error()
    {
        using var client = await AdminAsync();

        var response = await client.GetAsync(new Uri("/api/users?roleId=99", UriKind.Relative));

        // Not an empty page: a typo must not look like "no users hold this role".
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.ValidationError, await response.ReadErrorCodeAsync());
    }

    [Theory]
    [InlineData("username")]
    [InlineData("email")]
    [InlineData("firstName")]
    [InlineData("lastName")]
    [InlineData("role")]
    [InlineData("createdAt")]
    public async Task Every_whitelisted_sort_field_orders_in_both_directions(string field)
    {
        using var client = await AdminAsync();

        var ascending = await client.GetFromJsonAsync<PagedPayload>(
            new Uri($"/api/users?sortBy={field}&sortDirection=Ascending&pageSize=50", UriKind.Relative));
        var descending = await client.GetFromJsonAsync<PagedPayload>(
            new Uri($"/api/users?sortBy={field}&sortDirection=Descending&pageSize=50", UriKind.Relative));

        Assert.NotNull(ascending);
        Assert.NotNull(descending);
        Assert.Equal(ascending.TotalCount, descending.TotalCount);

        var ascendingKeys = ascending.Items.Select(item => SortKey(item, field)).ToList();
        Assert.Equal(ascendingKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase), ascendingKeys);

        // Reversing the direction has to actually reverse the order, not just accept the parameter.
        Assert.NotEqual(
            ascending.Items.Select(item => item.Id),
            descending.Items.Select(item => item.Id));
    }

    [Theory]
    [InlineData("passwordHash")]
    [InlineData("id; DROP TABLE Users")]
    [InlineData("1=1")]
    public async Task A_sort_field_outside_the_whitelist_is_refused(string field)
    {
        using var client = await AdminAsync();

        var response = await client.GetAsync(
            new Uri($"/api/users?sortBy={Uri.EscapeDataString(field)}", UriKind.Relative));

        // The whitelist is the entire defence against sort-field injection: client input selects a branch, it
        // never becomes part of a query string.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ErrorCodes.InvalidSortField, await response.ReadErrorCodeAsync());
    }

    [Fact]
    public async Task The_list_never_exposes_password_material()
    {
        using var client = await AdminAsync();

        var body = await client.GetStringAsync(new Uri("/api/users?pageSize=50", UriKind.Relative));

        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleted_users_are_absent_from_the_active_listing_and_present_in_the_admin_one()
    {
        using var admin = await AdminAsync();

        var created = await CreateUserAsync(admin, TestData.Username("hidden"));
        (await admin.DeleteAsync(new Uri($"/api/users/{created.Id}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var active = await admin.GetFromJsonAsync<PagedPayload>(
            new Uri($"/api/users?search={created.Username}", UriKind.Relative));
        Assert.NotNull(active);
        Assert.DoesNotContain(active.Items, item => item.Id == created.Id);

        var deleted = await admin.GetFromJsonAsync<PagedPayload>(
            new Uri($"/api/users/deleted?search={created.Username}", UriKind.Relative));
        Assert.NotNull(deleted);
        Assert.Contains(deleted.Items, item => item.Id == created.Id && item.IsDeleted);
    }

    [Fact]
    public async Task An_unknown_query_parameter_cannot_reveal_deleted_users()
    {
        using var admin = await AdminAsync();

        var created = await CreateUserAsync(admin, TestData.Username("flagprobe"));
        (await admin.DeleteAsync(new Uri($"/api/users/{created.Id}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        using var user = fixture.Api.CreateCookieClient();
        await user.AuthenticateAsUserAsync();

        // The parameter the earlier design accepted. It is now inert: there is nothing to bind it to, so it
        // cannot re-open the soft-delete filter (ADR-0004).
        var response = await user.GetAsync(
            new Uri($"/api/users?includeDeleted=true&search={created.Username}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedPayload>();
        Assert.NotNull(page);
        Assert.Empty(page.Items);
    }

    private static string SortKey(UserListItemPayload item, string field) => field switch
    {
        "username" => item.Username,
        "email" => item.Email,
        "firstName" => item.FirstName,
        "lastName" => item.LastName,
        "role" => item.Role,
        _ => item.CreatedAt.ToString("O"),
    };

    private async Task<HttpClient> AdminAsync()
    {
        var client = fixture.Api.CreateCookieClient();
        await client.AuthenticateAsAdminAsync();

        return client;
    }

    private static async Task<UserDetailsPayload> CreateUserAsync(
        HttpClient admin,
        string username,
        string firstName = "Test",
        string lastName = "User")
    {
        var response = await admin.PostAsJsonAsync(
            new Uri("/api/users", UriKind.Relative),
            new
            {
                username,
                email = $"{username}@example.com",
                firstName,
                lastName,
                password = "Created@123456",
                roleId = RoleIds.User,
            });

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<UserDetailsPayload>()
               ?? throw new InvalidOperationException("Create returned no payload.");
    }
}

public sealed record PagedPayload(
    List<UserListItemPayload> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage);

public sealed record UserListItemPayload(
    Guid Id,
    string Username,
    string Email,
    string FirstName,
    string LastName,
    int RoleId,
    string Role,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastModifiedAt,
    DateTimeOffset? LastLoginAt,
    bool IsLockedOut,
    DateTimeOffset? DeletedAt,
    string? DeletedBy);
