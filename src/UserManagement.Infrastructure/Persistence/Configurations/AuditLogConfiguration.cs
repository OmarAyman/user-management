using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", table =>
            table.HasCheckConstraint("CK_AuditLogs_Action", "[Action] BETWEEN 0 AND 4"));

        // Identity, clustered: the table is append-only, so ascending keys mean no page splits.
        builder.HasKey(log => log.Id).IsClustered();

        builder.Property(log => log.EntityName)
            .IsRequired()
            .HasMaxLength(UserConstraints.AuditEntityNameMaxLength);

        builder.Property(log => log.EntityId)
            .IsRequired()
            .HasMaxLength(UserConstraints.AuditEntityIdMaxLength);

        builder.Property(log => log.EntityDisplayName)
            .HasMaxLength(UserConstraints.NameMaxLength);

        builder.Property(log => log.Action)
            .IsRequired()
            .HasConversion<byte>();

        builder.Property(log => log.PerformedByUsername)
            .IsRequired()
            .HasMaxLength(UserConstraints.UsernameMaxLength);

        builder.Property(log => log.Timestamp)
            .IsRequired();

        builder.Property(log => log.IpAddress)
            .IsRequired()
            .HasMaxLength(UserConstraints.IpAddressMaxLength);

        builder.Property(log => log.CorrelationId)
            .HasMaxLength(UserConstraints.CorrelationIdMaxLength);

        // No foreign key to Users on purpose: history must survive whatever happens to the actor row, and an
        // FK on an append-only audit table only adds write cost and a delete-order dependency.
        builder.HasIndex(log => log.Timestamp)
            .IsDescending()
            .HasDatabaseName("IX_AuditLogs_Timestamp");

        builder.HasIndex(log => new { log.EntityName, log.EntityId, log.Timestamp })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_AuditLogs_Entity");

        builder.HasIndex(log => new { log.PerformedByUserId, log.Timestamp })
            .IsDescending(false, true)
            .HasDatabaseName("IX_AuditLogs_PerformedByUserId");
    }
}
