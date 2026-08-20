using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.IntegrationTests.TestSupport;

/// <summary>
/// Test data helpers. Usernames carry a random suffix so tests sharing one database cannot collide with each
/// other or with a previous run - the alternative, resetting the database between tests, costs more than it
/// buys at this size.
/// </summary>
public static class TestData
{
    public static string Username(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(50, prefix.Length + 13)];

    /// <summary>
    /// A user with an email derived from the username. Pass <paramref name="email"/> explicitly when a test
    /// needs to collide on exactly one identifier - otherwise a duplicate username is also a duplicate email,
    /// and the assertion cannot tell which index rejected the row.
    /// </summary>
    public static User NewUser(string username, int roleId = RoleIds.User, string? email = null) =>
        User.Create(username, email ?? $"{username}@example.com", "Test", "User", "hash-placeholder", roleId);
}
