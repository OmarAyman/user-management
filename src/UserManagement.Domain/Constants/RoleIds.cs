namespace UserManagement.Domain.Constants;

/// <summary>
/// Fixed role identifiers. The <c>Roles.Id</c> column is not an identity column: the values are seeded
/// explicitly so migrations, SQL scripts, seed data and tests all agree on which integer means which role.
/// </summary>
public static class RoleIds
{
    public const int Admin = 1;

    public const int User = 2;

    public const int ReadOnlyUser = 3;
}
