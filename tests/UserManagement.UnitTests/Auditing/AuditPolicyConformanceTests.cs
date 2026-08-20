using System.Reflection;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.UnitTests.Auditing;

/// <summary>
/// Holds the audit policy to the entities it governs.
/// </summary>
/// <remarks>
/// The other audit tests assert what today's payloads contain. These assert something a future change could
/// break: that any property whose <i>name</i> suggests a credential is either redacted or excluded, and that an
/// entity carrying such properties is either handled or explicitly never audited.
///
/// The point is the property nobody has written yet. Add <c>ApiToken</c> or <c>ClientSecret</c> to
/// <see cref="User"/> and the interceptor would happily serialise it into an audit row; this test fails
/// instead, and docs/13-audit-policy.md section 4.5 stops being an aspiration.
/// </remarks>
public sealed class AuditPolicyConformanceTests
{
    /// <summary>Entities the interceptor audits. Mirrors audit policy section 2.</summary>
    private static readonly Type[] AuditedEntities = [typeof(User)];

    /// <summary>Every entity in the domain, so a new one cannot slip past the policy unnoticed.</summary>
    private static readonly Type[] AllEntities =
    [
        typeof(User),
        typeof(Role),
        typeof(AuditLog),
        typeof(RefreshToken),
    ];

    [Fact]
    public void Every_credential_shaped_property_on_an_audited_entity_is_redacted_or_excluded()
    {
        var offenders = new List<string>();

        foreach (var entity in AuditedEntities)
        {
            foreach (var property in Properties(entity))
            {
                if (!LooksLikeCredential(property.Name))
                {
                    continue;
                }

                var handled = AuditRedaction.RedactedProperties.Contains(property.Name)
                              || AuditRedaction.ExcludedProperties.Contains(property.Name);

                if (!handled)
                {
                    offenders.Add($"{entity.Name}.{property.Name}");
                }
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void An_entity_carrying_credential_shaped_properties_is_either_audited_carefully_or_never_audited()
    {
        var offenders = new List<string>();

        foreach (var entity in AllEntities)
        {
            var credentialProperties = Properties(entity)
                .Where(property => LooksLikeCredential(property.Name))
                .Select(property => property.Name)
                .ToList();

            if (credentialProperties.Count == 0)
            {
                continue;
            }

            var audited = AuditedEntities.Contains(entity);
            var explicitlyNotAudited = AuditRedaction.NeverAuditedEntities.Contains(entity.Name);

            // RefreshToken.TokenHash is the live example: the entity is on the never-audited list, which is why
            // rotation never writes token material into the trail.
            if (!audited && !explicitlyNotAudited)
            {
                offenders.Add($"{entity.Name} ({string.Join(", ", credentialProperties)})");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_redacted_and_excluded_lists_name_properties_that_actually_exist()
    {
        var userProperties = Properties(typeof(User)).Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        // A stale entry is a quiet failure: a renamed property would drop out of the policy while the list still
        // looked complete.
        foreach (var name in AuditRedaction.RedactedProperties.Concat(AuditRedaction.ExcludedProperties))
        {
            Assert.Contains(name, userProperties);
        }
    }

    [Fact]
    public void The_two_lists_do_not_overlap()
    {
        // A property cannot be both redacted (recorded, value hidden) and excluded (not recorded at all); an
        // overlap would mean the policy document says two different things about the same field.
        Assert.Empty(AuditRedaction.RedactedProperties.Intersect(AuditRedaction.ExcludedProperties));
    }

    /// <summary>
    /// The persisted scalar properties of an entity.
    /// </summary>
    /// <remarks>
    /// Navigations are excluded because the interceptor cannot reach them: it walks the change tracker entry's
    /// scalar properties, so a collection like <c>User.RefreshTokens</c> is never serialised into a payload
    /// however its name reads. Leaving it in produced a false positive on the first run of this test - and
    /// narrowing the definition is the right resolution, because loosening the name check instead would have
    /// blinded it to a genuine scalar called <c>ApiToken</c>.
    /// </remarks>
    private static IEnumerable<PropertyInfo> Properties(Type entity) =>
        entity
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => IsScalar(property.PropertyType));

    private static bool IsScalar(Type type)
    {
        if (type == typeof(string) || type == typeof(byte[]))
        {
            return true;
        }

        // A collection is a navigation, never a column.
        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            return false;
        }

        // A reference to another entity is a navigation too.
        return type.Namespace != typeof(User).Namespace;
    }

    private static bool LooksLikeCredential(string propertyName) =>
        AuditRedaction.NeverPersistedNameFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
