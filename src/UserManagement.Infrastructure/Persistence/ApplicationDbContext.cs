using Microsoft.EntityFrameworkCore;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence;

/// <summary>
/// The single database context. Also serves as <see cref="IUnitOfWork"/>: a separate wrapper class would add a
/// file and a level of indirection without adding a capability, since committing is exactly what a context does.
/// </summary>
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configuration lives in one class per entity rather than in a single long method here.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
