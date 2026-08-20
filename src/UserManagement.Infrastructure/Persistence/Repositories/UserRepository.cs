using Microsoft.EntityFrameworkCore;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// User persistence. This is the <b>only</b> type in the solution that suspends the global soft-delete query
/// filter, and it does so in one private helper (<see cref="IncludingDeleted"/>) with three sanctioned
/// consumers: the Admin-only deleted listing, the Admin-only by-id lookup, and sign-in, which must see a
/// deleted row in order to refuse it rather than report "no such user".
/// </summary>
/// <remarks>
/// An architecture test scans the source tree and fails the build if <c>IgnoreQueryFilters</c> appears in any
/// other file, so soft-delete visibility cannot quietly leak into a new query (ADR-0004).
/// </remarks>
public sealed class UserRepository(ApplicationDbContext context) : IUserRepository
{
    public IQueryable<User> QueryActive() => context.Users.AsNoTracking();

    public IQueryable<User> QueryIncludingDeleted() => IncludingDeleted().AsNoTracking();

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        context.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken) =>
        IncludingDeleted()
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetForAuthenticationAsync(string username, CancellationToken cancellationToken) =>
        IncludingDeleted()
            .Include(user => user.Role)
            .FirstOrDefaultAsync(user => user.Username == username, cancellationToken);

    public Task<bool> IsUsernameTakenAsync(
        string username,
        Guid? excludeUserId,
        CancellationToken cancellationToken) =>
        // No IgnoreQueryFilters: the global filter is what scopes uniqueness to active users, which is exactly
        // the rule in ADR-0009.
        context.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Username == username && (excludeUserId == null || user.Id != excludeUserId),
                cancellationToken);

    public Task<bool> IsEmailTakenAsync(string email, Guid? excludeUserId, CancellationToken cancellationToken) =>
        context.Users
            .AsNoTracking()
            .AnyAsync(
                user => user.Email == email && (excludeUserId == null || user.Id != excludeUserId),
                cancellationToken);

    public Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken) =>
        context.Users
            .AsNoTracking()
            .CountAsync(user => user.RoleId == RoleIds.Admin, cancellationToken);

    public void Add(User user) => context.Users.Add(user);

    /// <summary>
    /// The single soft-delete opt-out in the solution. Tracked, because two of its consumers load an entity in
    /// order to change it.
    /// </summary>
    private IQueryable<User> IncludingDeleted() => context.Users.IgnoreQueryFilters();
}
