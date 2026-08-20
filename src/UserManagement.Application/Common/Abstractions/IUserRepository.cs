using UserManagement.Domain.Entities;

namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Read and write access to users. Purpose-built rather than a generic repository: the method names say what
/// the caller means, and the soft-delete opt-out is one named method instead of a boolean nobody notices.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Active users only. The EF Core global query filter already excludes deleted rows; this method exists so
    /// the intent is explicit at every call site.
    /// </summary>
    IQueryable<User> QueryActive();

    /// <summary>
    /// Includes soft-deleted users. This is the <b>only</b> place in the solution that suspends the global
    /// query filter, and it has exactly two legitimate callers: the Admin-only deleted-users query, and
    /// sign-in, which must see a deleted row in order to refuse it rather than report "no such user".
    /// An architecture test fails the build if <c>IgnoreQueryFilters</c> appears anywhere else (ADR-0004).
    /// </summary>
    IQueryable<User> QueryIncludingDeleted();

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Loads a user for administration, including a soft-deleted one. Admin-gated at the endpoint.</summary>
    Task<User?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a user by username for sign-in, including soft-deleted rows and their refresh tokens.
    /// Case-insensitive, matching the database collation.
    /// </summary>
    Task<User?> GetForAuthenticationAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// True when an <b>active</b> user already holds this username. Uniqueness is scoped to active rows, so a
    /// soft-deleted user's username is available again (ADR-0009). The filtered unique index remains the final
    /// authority under a race; this check exists to return a clean 409 instead of a database error.
    /// </summary>
    Task<bool> IsUsernameTakenAsync(string username, Guid? excludeUserId, CancellationToken cancellationToken);

    /// <summary>True when an active user already holds this email. Same scoping as the username check.</summary>
    Task<bool> IsEmailTakenAsync(string email, Guid? excludeUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Counts active administrators. Used inside the mutating transaction to enforce that the system cannot be
    /// left with zero administrators (BR-02, BR-03).
    /// </summary>
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    void Add(User user);

    /// <summary>
    /// Tells the change tracker which version of the row the caller edited, so a stale write is refused.
    /// </summary>
    /// <remarks>
    /// The concurrency token has to be applied through the tracker rather than assigned to the entity: the
    /// column is engine-maintained, and what matters is the value EF puts in the UPDATE's WHERE clause. That
    /// is an EF concept, so it lives behind this port instead of leaking DbContext into a handler (ADR-0013).
    /// </remarks>
    void ApplyConcurrencyToken(User user, byte[] rowVersion);
}
