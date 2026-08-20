using Microsoft.EntityFrameworkCore;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// Reads audit entries. There is no write path here - entries are created by
/// <c>AuditSaveChangesInterceptor</c>, so auditing cannot be skipped by a caller that forgets it.
/// </summary>
public sealed class AuditLogRepository(ApplicationDbContext context) : IAuditLogRepository
{
    public IQueryable<AuditLog> Query() => context.AuditLogs.AsNoTracking();
}
