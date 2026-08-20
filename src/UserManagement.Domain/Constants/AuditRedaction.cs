using UserManagement.Domain.Entities;

namespace UserManagement.Domain.Constants;

/// <summary>
/// The field policy from <c>docs/13-audit-policy.md</c>, expressed once in code. The audit interceptor and the
/// tests that enforce the policy both read these sets, so the document, the implementation and the assertions
/// cannot drift apart without a test failing.
/// </summary>
public static class AuditRedaction
{
    /// <summary>Written in place of a redacted value, so the change is visible but the value is not.</summary>
    public const string RedactedMarker = "***";

    /// <summary>
    /// Properties whose change is recorded but whose value is replaced by <see cref="RedactedMarker"/>.
    /// Redaction rather than exclusion: that a password changed is exactly what an auditor needs to see.
    /// </summary>
    public static readonly IReadOnlySet<string> RedactedProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(User.PasswordHash),
        nameof(User.SecurityStamp),
    };

    /// <summary>
    /// Properties that never appear in an audit payload and never cause an audit row on their own. Either they
    /// duplicate the row's own metadata, are engine-maintained, or churn on every login.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedProperties = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(User.CreatedAt),
        nameof(User.CreatedBy),
        nameof(User.LastModifiedAt),
        nameof(User.LastModifiedBy),
        nameof(User.RowVersion),
        nameof(User.LastLoginAt),
        nameof(User.FailedLoginAttempts),
        nameof(User.LockoutEndAt),
    };

    /// <summary>
    /// Entities that are never audited at all. Refresh-token rows are created and rotated mechanically, so
    /// auditing them would flood the trail and place token lifecycle data next to the user data an auditor
    /// reads. Their events go to the security log instead.
    /// </summary>
    public static readonly IReadOnlySet<string> NeverAuditedEntities = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(RefreshToken),
        nameof(AuditLog),
        nameof(Role),
    };

    /// <summary>
    /// Substrings that must never appear as a property name in a persisted audit payload. Asserted by test,
    /// so a future property called <c>Token</c> or <c>Secret</c> cannot silently start being stored.
    /// </summary>
    public static readonly IReadOnlyList<string> NeverPersistedNameFragments =
    [
        "password",
        "token",
        "secret",
        "credential",
        "cookie",
        "authorization",
    ];
}
