namespace UserManagement.Application.Common.Security;

/// <summary>
/// Authorization policy names. Two policies with identical requirements today, kept separate because
/// "who may administer users" and "who may read the audit trail" are different questions that commonly
/// diverge - an auditor role is the usual next request. Two names cost nothing now and prevent a rewrite later.
/// </summary>
public static class Policies
{
    /// <summary>Create, edit, delete, restore users and change roles. Admin only.</summary>
    public const string ManageUsers = "users:manage";

    /// <summary>Read the audit trail. Admin only.</summary>
    public const string ViewAuditLogs = "audit:view";
}
