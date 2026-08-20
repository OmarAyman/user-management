using Microsoft.EntityFrameworkCore;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Repositories;

public sealed class RoleRepository(ApplicationDbContext context) : IRoleRepository
{
    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken) =>
        await context.Roles
            .AsNoTracking()
            .OrderBy(role => role.Id)
            .ToListAsync(cancellationToken);

    public Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken) =>
        context.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    public Task<bool> ExistsAsync(int roleId, CancellationToken cancellationToken) =>
        context.Roles
            .AsNoTracking()
            .AnyAsync(role => role.Id == roleId, cancellationToken);
}
