namespace UserManagement.Domain.Common;

/// <summary>
/// Marks an entity that is never physically deleted. Implementers are excluded from queries by an EF Core
/// global query filter; see <c>docs/02-architecture.md</c> section 7 for the single opt-out.
/// </summary>
/// <remarks>
/// The state is read-only here on purpose. Transitions happen through domain methods
/// (<see cref="Entities.User.SoftDelete"/>, <see cref="Entities.User.Restore"/>) so the flag and its
/// timestamp can never disagree.
/// </remarks>
public interface ISoftDeletable
{
    bool IsDeleted { get; }

    DateTimeOffset? DeletedAt { get; }

    string? DeletedBy { get; }
}
