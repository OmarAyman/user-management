using UserManagement.Domain.Constants;

namespace UserManagement.Domain.Entities;

/// <summary>
/// A persisted role. The set is closed - there is no role CRUD API - because the assignment fixes three
/// roles and the authorization policies are written against them. Persisting them anyway keeps role names out
/// of the code as literals and lets the UI populate filters from data.
/// </summary>
public sealed class Role
{
    // EF Core materialisation.
    private Role()
    {
        Name = string.Empty;
    }

    private Role(int id, string name)
    {
        Id = id;
        Name = name;
    }

    public int Id { get; private set; }

    public string Name { get; private set; }

    /// <summary>The seeded role set, used by the model configuration and the seeder.</summary>
    public static IReadOnlyList<Role> Seed { get; } =
    [
        new Role(RoleIds.Admin, RoleNames.Admin),
        new Role(RoleIds.User, RoleNames.User),
        new Role(RoleIds.ReadOnlyUser, RoleNames.ReadOnlyUser),
    ];
}
