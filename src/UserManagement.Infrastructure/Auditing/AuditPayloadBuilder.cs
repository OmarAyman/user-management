using System.Text.Json;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Enums;

namespace UserManagement.Infrastructure.Auditing;

/// <summary>One property's before and after value, as read from the change tracker.</summary>
public readonly record struct PropertyChange(string Name, object? OldValue, object? NewValue);

/// <summary>The serialised audit payload, plus whether anything audit-worthy actually changed.</summary>
public sealed record AuditPayload(string? OldValues, string? NewValues, bool HasAuditableChanges)
{
    public static AuditPayload None { get; } = new(null, null, false);
}

/// <summary>
/// Turns a set of property changes into the JSON stored on an audit row, applying the field policy from
/// <c>docs/13-audit-policy.md</c>.
/// </summary>
/// <remarks>
/// Deliberately free of any EF Core type. The policy's most important claims - that password material is
/// redacted and that noise columns never appear - are therefore unit-testable without a database, which is why
/// the extraction of changes from the change tracker lives in the interceptor and the decision-making lives here.
/// </remarks>
public static class AuditPayloadBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        // The payload is a dictionary, so the key policy is what governs casing here - PropertyNamingPolicy
        // applies to object members and would leave these keys PascalCase, out of step with every other
        // JSON the API emits.
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static AuditPayload Build(AuditAction action, IReadOnlyCollection<PropertyChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var included = changes
            .Where(change => !AuditRedaction.ExcludedProperties.Contains(change.Name))
            .ToList();

        if (included.Count == 0)
        {
            return AuditPayload.None;
        }

        var oldValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var newValues = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var change in included)
        {
            var redacted = AuditRedaction.RedactedProperties.Contains(change.Name);

            // Redaction rather than exclusion: that a password changed is exactly what an auditor needs to
            // see; what it changed to is exactly what they must never learn.
            oldValues[change.Name] = redacted ? AuditRedaction.RedactedMarker : Normalise(change.OldValue);
            newValues[change.Name] = redacted ? AuditRedaction.RedactedMarker : Normalise(change.NewValue);
        }

        // An insert has no previous state; everything else records both sides, including a soft delete, where
        // the transition of IsDeleted is the interesting part.
        var oldJson = action == AuditAction.Insert ? null : Serialise(oldValues);
        var newJson = Serialise(newValues);

        return new AuditPayload(oldJson, newJson, true);
    }

    /// <summary>
    /// Values are serialised as strings for anything that is not a JSON primitive, so a Guid or a
    /// DateTimeOffset renders as a readable value rather than as an object graph.
    /// </summary>
    private static object? Normalise(object? value) => value switch
    {
        null => null,
        string or bool or int or long or short or byte or decimal or double or float => value,
        DateTimeOffset offset => offset.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        byte[] bytes => Convert.ToBase64String(bytes),
        _ => value.ToString(),
    };

    private static string Serialise(Dictionary<string, object?> values) =>
        JsonSerializer.Serialize(values, SerializerOptions);
}
