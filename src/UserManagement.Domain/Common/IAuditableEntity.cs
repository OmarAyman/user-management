namespace UserManagement.Domain.Common;

/// <summary>
/// Marks an entity whose creation and modification are stamped centrally.
/// </summary>
/// <remarks>
/// The setters are public because <c>AuditableEntitySaveChangesInterceptor</c> populates them, and it lives
/// in another assembly. Nothing else assigns them: handlers never touch timestamps, which is the whole point
/// of stamping in one place.
/// </remarks>
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }

    string CreatedBy { get; set; }

    DateTimeOffset? LastModifiedAt { get; set; }

    string? LastModifiedBy { get; set; }
}
