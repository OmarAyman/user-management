using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens", table =>
            table.HasCheckConstraint("CK_RefreshTokens_RevocationReason", "[RevocationReason] BETWEEN 0 AND 6"));

        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id)
            .ValueGeneratedNever();

        // Fixed-length hex: char(64) rather than nvarchar, because the value is always exactly 64 ASCII
        // characters and a fixed-width key compares and indexes better.
        builder.Property(token => token.TokenHash)
            .IsRequired()
            .IsUnicode(false)
            .IsFixedLength()
            .HasMaxLength(UserConstraints.TokenHashLength);

        builder.Property(token => token.FamilyId)
            .IsRequired();

        builder.Property(token => token.CreatedByIp)
            .IsRequired()
            .HasMaxLength(UserConstraints.IpAddressMaxLength);

        builder.Property(token => token.RevokedByIp)
            .HasMaxLength(UserConstraints.IpAddressMaxLength);

        builder.Property(token => token.RevocationReason)
            .HasConversion<byte?>();

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("UQ_RefreshTokens_TokenHash");

        builder.HasIndex(token => token.UserId)
            .IncludeProperties(token => new { token.ExpiresAt, token.RevokedAt })
            .HasDatabaseName("IX_RefreshTokens_UserId");

        // Reuse detection revokes a whole family, so that lookup needs its own index.
        builder.HasIndex(token => token.FamilyId)
            .HasDatabaseName("IX_RefreshTokens_FamilyId");

        // Self-referencing FK with no navigation: it exists so a rotation chain is navigable in SQL, and it
        // must not cascade (SQL Server rejects self-referencing cascades anyway).
        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(token => token.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
