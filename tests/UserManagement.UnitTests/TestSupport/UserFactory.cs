using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.UnitTests.TestSupport;

/// <summary>
/// Builds domain objects for tests that need a loaded <see cref="User.Role"/> navigation.
/// </summary>
/// <remarks>
/// The navigation has a private setter because only EF Core should populate it in production. Reflection here
/// is the honest trade: the alternative is widening the entity's surface purely for tests, which would make
/// the production type worse in order to make a test easier.
/// </remarks>
public static class UserFactory
{
    public static User Create(
        string username = "asmith",
        string passwordHash = "hash",
        string roleName = RoleNames.User)
    {
        var role = Role.Seed.Single(candidate =>
            string.Equals(candidate.Name, roleName, StringComparison.Ordinal));

        var user = User.Create(
            username,
            $"{username}@example.com",
            "Alex",
            "Smith",
            passwordHash,
            role.Id);

        typeof(User).GetProperty(nameof(User.Role))!.SetValue(user, role);

        return user;
    }
}
