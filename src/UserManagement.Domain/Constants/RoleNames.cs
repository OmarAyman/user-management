namespace UserManagement.Domain.Constants;

/// <summary>
/// The three role names, as persisted in the <c>Roles</c> table and as they appear in the <c>role</c> JWT
/// claim. Referenced instead of inline literals so a rename is a compile error rather than a silent
/// authorization hole.
/// </summary>
public static class RoleNames
{
    public const string Admin = "Admin";

    public const string User = "User";

    public const string ReadOnlyUser = "ReadOnlyUser";

    /// <summary>All role names, in seed order.</summary>
    public static readonly IReadOnlyList<string> All = [Admin, User, ReadOnlyUser];
}
