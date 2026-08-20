namespace UserManagement.Domain.Constants;

/// <summary>
/// Actor names used for stamping and auditing when there is no authenticated caller. Distinct values so a
/// reviewer can tell a seeded row from an unauthenticated code path at a glance.
/// </summary>
public static class SystemActors
{
    /// <summary>Background or startup work with no HTTP context.</summary>
    public const string System = "system";

    /// <summary>Rows created by the database seeder.</summary>
    public const string Seed = "seed";

    /// <summary>Used where an IP address is required but unavailable (seeder, unit tests).</summary>
    public const string UnknownIpAddress = "unknown";
}
