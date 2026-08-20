using UserManagement.Domain.Entities;

namespace UserManagement.Application.Common.Abstractions;

/// <summary>
/// Read-only access to the closed role set. There is no write path: the three roles are seeded reference data,
/// and adding role CRUD would mean revisiting the authorization policies written against them.
/// </summary>
public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken);

    Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(int roleId, CancellationToken cancellationToken);
}
