using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Common;
using UserManagement.Domain.Constants;

namespace UserManagement.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps created and modified metadata on every <see cref="IAuditableEntity"/>.
/// </summary>
/// <remarks>
/// Centralised here rather than in each handler for the obvious reason: a handler that forgets is a row with a
/// missing timestamp, and nobody notices until someone tries to sort by it. Handlers never set these fields.
/// </remarks>
public sealed class AuditableEntitySaveChangesInterceptor(
    ICurrentUserService currentUser,
    IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var actor = currentUser.Username ?? SystemActors.System;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;

                    // An explicitly provided origin is preserved - the seeder marks filler rows that way.
                    // Everything else is stamped with the authenticated caller, or "system" outside a request.
                    if (string.IsNullOrEmpty(entry.Entity.CreatedBy))
                    {
                        entry.Entity.CreatedBy = actor;
                    }

                    break;

                case EntityState.Modified:
                    entry.Entity.LastModifiedAt = now;
                    entry.Entity.LastModifiedBy = actor;
                    break;

                default:
                    break;
            }
        }
    }
}
