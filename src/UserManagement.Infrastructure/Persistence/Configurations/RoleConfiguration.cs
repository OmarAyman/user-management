using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.HasKey(role => role.Id);

        // Not an identity column: the ids are fixed reference values, so migrations, SQL scripts, seed data
        // and tests all agree that 1 means Admin.
        builder.Property(role => role.Id)
            .ValueGeneratedNever();

        builder.Property(role => role.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(role => role.Name)
            .IsUnique()
            .HasDatabaseName("UQ_Roles_Name");

        // Reference data belongs in the migration, so a fresh database and the generated SQL script contain
        // the roles without anyone having to remember to run a seeder first.
        builder.HasData(Role.Seed.Select(role => new { role.Id, role.Name }));
    }
}
