using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;
using UserManagement.Domain.Enums;
using UserManagement.Infrastructure.Auditing;

namespace UserManagement.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes the audit trail from the change tracker, implementing the policy in <c>docs/13-audit-policy.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Auditing is a property of persistence, not of the caller: a use case added later is audited whether its
/// author thought about it or not. That is the whole reason this is an interceptor and not a service someone
/// has to remember to call.
/// </para>
/// <para>
/// Rows are built and added <em>before</em> the save, so they commit in the same transaction as the change they
/// describe - there is no window in which a change exists without its audit row. This works because entity keys
/// are client-generated (version 7 GUIDs), so the target id is already known; if a future audited entity used a
/// database-generated key, its rows would have to be written after the save instead.
/// </para>
/// </remarks>
public sealed class AuditSaveChangesInterceptor(
    ICurrentUserService currentUser,
    IClientInfoProvider clientInfo,
    IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        WriteAuditRows(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        WriteAuditRows(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void WriteAuditRows(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Materialised before adding anything: appending to the change tracker while enumerating it would
        // throw, and the audit rows themselves must not be re-examined.
        var entries = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is not AuditLog)
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => !AuditRedaction.NeverAuditedEntities.Contains(entry.Metadata.ClrType.Name))
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var now = clock.UtcNow;
        var actorId = currentUser.UserId;
        var actorName = currentUser.Username ?? SystemActors.System;
        var ipAddress = clientInfo.IpAddress;
        var correlationId = clientInfo.CorrelationId;

        foreach (var entry in entries)
        {
            foreach (var row in BuildRowsFor(entry, now, actorId, actorName, ipAddress, correlationId))
            {
                context.Add(row);
            }
        }
    }

    private static IEnumerable<AuditLog> BuildRowsFor(
        EntityEntry entry,
        DateTimeOffset now,
        Guid? actorId,
        string actorName,
        string ipAddress,
        string? correlationId)
    {
        var action = ResolveAction(entry);
        var changes = ExtractChanges(entry, action);
        var payload = AuditPayloadBuilder.Build(action, changes);

        // A save that only touched excluded columns - a login stamping LastLoginAt, for instance - is not an
        // audit event. Recording it would bury the changes that matter.
        if (!payload.HasAuditableChanges)
        {
            yield break;
        }

        var entityId = ResolveEntityId(entry);
        var displayName = ResolveDisplayName(entry);

        yield return AuditLog.Create(
            entry.Metadata.ClrType.Name,
            entityId,
            displayName,
            action,
            actorId,
            actorName,
            now,
            ipAddress,
            payload.OldValues,
            payload.NewValues,
            correlationId);

        // A role change also gets its own row, so privilege movement is findable with a single-column filter
        // instead of by diffing JSON. Duplication is the point.
        var roleChange = changes.FirstOrDefault(change =>
            string.Equals(change.Name, nameof(User.RoleId), StringComparison.Ordinal));

        if (action == AuditAction.Update && roleChange.Name is not null)
        {
            var rolePayload = AuditPayloadBuilder.Build(AuditAction.Update, [roleChange]);

            yield return AuditLog.Create(
                entry.Metadata.ClrType.Name,
                entityId,
                displayName,
                AuditAction.RoleChange,
                actorId,
                actorName,
                now,
                ipAddress,
                rolePayload.OldValues,
                rolePayload.NewValues,
                correlationId);
        }
    }

    /// <summary>
    /// Maps the tracked state onto an audited action. A soft delete is physically an update, but it is recorded
    /// as <see cref="AuditAction.Delete"/> because the trail records intent and "deleted" is what a reviewer
    /// searches for.
    /// </summary>
    private static AuditAction ResolveAction(EntityEntry entry)
    {
        if (entry.State == EntityState.Added)
        {
            return AuditAction.Insert;
        }

        if (entry.State == EntityState.Deleted)
        {
            // No use case hard-deletes an audited entity. Recorded rather than ignored, so that if one ever
            // appears the trail shows it instead of hiding it.
            return AuditAction.Delete;
        }

        var isDeleted = entry.Properties.FirstOrDefault(property =>
            string.Equals(property.Metadata.Name, nameof(User.IsDeleted), StringComparison.Ordinal));

        if (isDeleted is { IsModified: true })
        {
            var wasDeleted = isDeleted.OriginalValue is true;
            var nowDeleted = isDeleted.CurrentValue is true;

            if (!wasDeleted && nowDeleted)
            {
                return AuditAction.Delete;
            }

            if (wasDeleted && !nowDeleted)
            {
                return AuditAction.Restore;
            }
        }

        return AuditAction.Update;
    }

    private static List<PropertyChange> ExtractChanges(EntityEntry entry, AuditAction action)
    {
        var candidates = entry.Properties.Where(property =>
            !property.Metadata.IsPrimaryKey()
            && !AuditRedaction.ExcludedProperties.Contains(property.Metadata.Name));

        // An insert records the new state in full; everything else records only what moved.
        if (action != AuditAction.Insert)
        {
            candidates = candidates.Where(property => property.IsModified);
        }

        return candidates
            .Select(property => new PropertyChange(
                property.Metadata.Name,
                action == AuditAction.Insert ? null : property.OriginalValue,
                property.CurrentValue))
            .ToList();
    }

    private static string ResolveEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();

        if (key is null)
        {
            return string.Empty;
        }

        var values = key.Properties
            .Select(property => entry.Property(property.Name).CurrentValue?.ToString() ?? string.Empty);

        return string.Join(':', values);
    }

    /// <summary>
    /// The target's username as it was at the time of the action. A readability aid only: audit identity is the
    /// immutable id, which is what lets a soft-deleted username be reused without making history ambiguous.
    /// </summary>
    private static string? ResolveDisplayName(EntityEntry entry) =>
        entry.Entity is User user ? user.Username : null;
}
