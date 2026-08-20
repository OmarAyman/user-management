using System.Text.Json;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Infrastructure.Auditing;

namespace UserManagement.UnitTests.Auditing;

/// <summary>
/// Enforces the field policy in <c>docs/13-audit-policy.md</c>. These are the tests that make the policy a
/// guarantee rather than a document: the redaction and exclusion claims fail the build if the behaviour drifts.
/// </summary>
public sealed class AuditPayloadBuilderTests
{
    [Fact]
    public void An_insert_records_the_new_state_and_no_previous_state()
    {
        var payload = AuditPayloadBuilder.Build(
            AuditAction.Insert,
            [new PropertyChange(nameof(User.Username), null, "asmith")]);

        Assert.True(payload.HasAuditableChanges);
        Assert.Null(payload.OldValues);
        Assert.Equal("asmith", ReadValue(payload.NewValues, "username"));
    }

    [Fact]
    public void An_update_records_both_sides_of_every_change()
    {
        var payload = AuditPayloadBuilder.Build(
            AuditAction.Update,
            [new PropertyChange(nameof(User.Email), "old@example.com", "new@example.com")]);

        Assert.Equal("old@example.com", ReadValue(payload.OldValues, "email"));
        Assert.Equal("new@example.com", ReadValue(payload.NewValues, "email"));
    }

    [Fact]
    public void A_soft_delete_records_both_sides_because_the_transition_is_the_point()
    {
        var payload = AuditPayloadBuilder.Build(
            AuditAction.Delete,
            [new PropertyChange(nameof(User.IsDeleted), false, true)]);

        Assert.Equal("False", ReadValue(payload.OldValues, "isDeleted"));
        Assert.Equal("True", ReadValue(payload.NewValues, "isDeleted"));
    }

    [Fact]
    public void The_password_hash_is_redacted_on_both_sides()
    {
        var payload = AuditPayloadBuilder.Build(
            AuditAction.Update,
            [new PropertyChange(nameof(User.PasswordHash), "AQAAAAIAAYagAAAA-old", "AQAAAAIAAYagAAAA-new")]);

        // The event stays visible; the material does not. That is the point of redacting rather than excluding.
        Assert.Equal(AuditRedaction.RedactedMarker, ReadValue(payload.OldValues, "passwordHash"));
        Assert.Equal(AuditRedaction.RedactedMarker, ReadValue(payload.NewValues, "passwordHash"));
        Assert.DoesNotContain("AQAAAA", payload.NewValues, StringComparison.Ordinal);
        Assert.DoesNotContain("AQAAAA", payload.OldValues, StringComparison.Ordinal);
    }

    [Fact]
    public void The_security_stamp_is_redacted()
    {
        var stamp = Guid.NewGuid();

        var payload = AuditPayloadBuilder.Build(
            AuditAction.Update,
            [new PropertyChange(nameof(User.SecurityStamp), Guid.NewGuid(), stamp)]);

        Assert.Equal(AuditRedaction.RedactedMarker, ReadValue(payload.NewValues, "securityStamp"));
        Assert.DoesNotContain(stamp.ToString(), payload.NewValues, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(User.RowVersion))]
    [InlineData(nameof(User.LastLoginAt))]
    [InlineData(nameof(User.FailedLoginAttempts))]
    [InlineData(nameof(User.LockoutEndAt))]
    [InlineData(nameof(User.CreatedAt))]
    [InlineData(nameof(User.CreatedBy))]
    [InlineData(nameof(User.LastModifiedAt))]
    [InlineData(nameof(User.LastModifiedBy))]
    public void Excluded_properties_never_appear_and_never_create_an_audit_row(string propertyName)
    {
        var payload = AuditPayloadBuilder.Build(
            AuditAction.Update,
            [new PropertyChange(propertyName, "before", "after")]);

        // A save that only touched excluded columns - a sign-in stamping LastLoginAt, for instance - is not an
        // audit event, and recording it would bury the changes that matter.
        Assert.False(payload.HasAuditableChanges);
        Assert.Null(payload.OldValues);
        Assert.Null(payload.NewValues);
    }

    [Fact]
    public void An_excluded_property_alongside_an_audited_one_is_dropped_from_the_payload()
    {
        var payload = AuditPayloadBuilder.Build(
            AuditAction.Update,
            [
                new PropertyChange(nameof(User.Email), "a@example.com", "b@example.com"),
                new PropertyChange(nameof(User.LastLoginAt), null, DateTimeOffset.UtcNow),
            ]);

        Assert.True(payload.HasAuditableChanges);
        Assert.NotNull(ReadValue(payload.NewValues, "email"));
        Assert.Null(ReadValue(payload.NewValues, "lastLoginAt"));
    }

    [Fact]
    public void Payload_keys_are_camel_case_like_every_other_json_the_api_emits()
    {
        var payload = AuditPayloadBuilder.Build(
            AuditAction.Insert,
            [new PropertyChange(nameof(User.FirstName), null, "Alex")]);

        Assert.Contains("\"firstName\"", payload.NewValues, StringComparison.Ordinal);
        Assert.DoesNotContain("\"FirstName\"", payload.NewValues, StringComparison.Ordinal);
    }

    [Fact]
    public void No_change_at_all_produces_no_payload()
    {
        var payload = AuditPayloadBuilder.Build(AuditAction.Update, []);

        Assert.False(payload.HasAuditableChanges);
    }

    [Fact]
    public void Values_are_rendered_as_readable_scalars()
    {
        var timestamp = new DateTimeOffset(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

        var payload = AuditPayloadBuilder.Build(
            AuditAction.Update,
            [
                new PropertyChange(nameof(User.DeletedAt), null, timestamp),
                new PropertyChange(nameof(User.RoleId), 2, 1),
            ]);

        Assert.Equal("2026-08-20T09:00:00.0000000+00:00", ReadValue(payload.NewValues, "deletedAt"));
        Assert.Equal("1", ReadValue(payload.NewValues, "roleId"));
    }

    private static string? ReadValue(string? json, string key)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);

        return document.RootElement.TryGetProperty(key, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => value.GetString(),
                _ => value.ToString(),
            }
            : null;
    }
}
