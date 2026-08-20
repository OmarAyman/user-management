using UserManagement.Domain.Enums;

namespace UserManagement.Domain.Entities;

/// <summary>
/// One recorded change to an audited entity. Append-only: there is no mutating method, no public setter after
/// construction, no repository update path and no HTTP route that writes to it.
/// See <c>docs/13-audit-policy.md</c> for what is captured, redacted and never stored.
/// </summary>
public sealed class AuditLog
{
    // EF Core materialisation.
    private AuditLog()
    {
        EntityName = string.Empty;
        EntityId = string.Empty;
        PerformedByUsername = string.Empty;
        IpAddress = string.Empty;
    }

    private AuditLog(
        string entityName,
        string entityId,
        string? entityDisplayName,
        AuditAction action,
        Guid? performedByUserId,
        string performedByUsername,
        DateTimeOffset timestamp,
        string ipAddress,
        string? oldValues,
        string? newValues,
        string? correlationId)
    {
        EntityName = entityName;
        EntityId = entityId;
        EntityDisplayName = entityDisplayName;
        Action = action;
        PerformedByUserId = performedByUserId;
        PerformedByUsername = performedByUsername;
        Timestamp = timestamp;
        IpAddress = ipAddress;
        OldValues = oldValues;
        NewValues = newValues;
        CorrelationId = correlationId;
    }

    public long Id { get; private set; }

    public string EntityName { get; private set; }

    /// <summary>
    /// The target's immutable identifier - for users, the <c>UserId</c>. Never a username: audit identity must
    /// not depend on a mutable, reusable label (ADR-0009).
    /// </summary>
    public string EntityId { get; private set; }

    /// <summary>
    /// The target's username as it was when the action happened. A readability aid so the trail can be read
    /// without a join, and explicitly not a key.
    /// </summary>
    public string? EntityDisplayName { get; private set; }

    public AuditAction Action { get; private set; }

    /// <summary>Null only for system and seed operations, which have no authenticated actor.</summary>
    public Guid? PerformedByUserId { get; private set; }

    /// <summary>Denormalised on purpose: history must stay readable if the actor is later deleted.</summary>
    public string PerformedByUsername { get; private set; }

    public DateTimeOffset Timestamp { get; private set; }

    public string IpAddress { get; private set; }

    /// <summary>JSON object of changed properties only, redacted per the audit policy. Null on insert.</summary>
    public string? OldValues { get; private set; }

    /// <summary>JSON object of changed properties only, redacted per the audit policy.</summary>
    public string? NewValues { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditLog Create(
        string entityName,
        string entityId,
        string? entityDisplayName,
        AuditAction action,
        Guid? performedByUserId,
        string performedByUsername,
        DateTimeOffset timestamp,
        string ipAddress,
        string? oldValues,
        string? newValues,
        string? correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(performedByUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);

        return new AuditLog(
            entityName,
            entityId,
            entityDisplayName,
            action,
            performedByUserId,
            performedByUsername,
            timestamp,
            ipAddress,
            oldValues,
            newValues,
            correlationId);
    }
}
