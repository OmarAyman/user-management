namespace UserManagement.Infrastructure.Persistence.Seeding;

/// <summary>
/// Demo account settings. Supplied by <c>appsettings.Development.json</c>, which contains no real secret: these
/// are documented, development-only credentials for a reviewer to sign in with.
/// </summary>
/// <remarks>
/// When no passwords are configured the seeder creates no users at all and says so in the log. That is the safe
/// default: a deployed environment must not acquire well-known accounts because someone forgot a setting.
/// </remarks>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Master switch. Off by default, and only ever turned on for Development.</summary>
    public bool Enabled { get; set; }

    public string? AdminPassword { get; set; }

    public string? UserPassword { get; set; }

    public string? ReadOnlyPassword { get; set; }

    /// <summary>
    /// Extra users so search, sorting, paging and the role filter are demonstrable without manual data entry.
    /// </summary>
    public int SampleUserCount { get; set; }
}
