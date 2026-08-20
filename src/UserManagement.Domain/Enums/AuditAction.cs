namespace UserManagement.Domain.Enums;

/// <summary>
/// The audited operations. Stored as <c>tinyint</c> with a check constraint, so the numeric values are part
/// of the database contract and must not be reordered.
/// </summary>
public enum AuditAction : byte
{
    Insert = 0,

    Update = 1,

    /// <summary>
    /// A soft delete. Recorded as <see cref="Delete"/> rather than <see cref="Update"/> because the trail
    /// records intent, and "deleted" is what a reviewer searches for.
    /// </summary>
    Delete = 2,

    Restore = 3,

    /// <summary>
    /// Emitted in addition to the <see cref="Update"/> row, so privilege changes are findable with a
    /// single-column filter instead of by diffing JSON.
    /// </summary>
    RoleChange = 4,
}
