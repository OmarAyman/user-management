using UserManagement.Domain.Entities;

namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Read access to the audit trail.
/// </summary>
/// <remarks>
/// Read-only on purpose, and the absence of any write, update or delete member is part of the design rather
/// than an omission: audit rows are written by the save-changes interceptor, and nothing in the application
/// should be able to alter one. There is no method here for a future caller to reach for (audit policy
/// section 5).
/// </remarks>
public interface IAuditLogRepository
{
    /// <summary>Untracked, because audit entries are only ever read.</summary>
    IQueryable<AuditLog> Query();
}
