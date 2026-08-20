using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UserManagement.Domain.Constants;
using UserManagement.Domain.Entities;

namespace UserManagement.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// Case-insensitive, accent-sensitive. Set explicitly on the identifier columns so uniqueness and login
    /// lookups behave the same way regardless of the collation the server or database happens to have.
    /// </summary>
    private const string CaseInsensitiveCollation = "SQL_Latin1_General_CP1_CI_AS";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users", table =>
            // A soft-delete flag that can disagree with its timestamp is a reporting bug waiting to happen.
            table.HasCheckConstraint(
                "CK_Users_DeletedConsistency",
                "([IsDeleted] = 0 AND [DeletedAt] IS NULL) OR ([IsDeleted] = 1 AND [DeletedAt] IS NOT NULL)"));

        // The clustered index is on CreatedAt, because the default listing is newest-first and that is the
        // range scan worth optimising. The key stays non-clustered.
        builder.HasKey(user => user.Id).IsClustered(false);

        builder.Property(user => user.Id)
            .ValueGeneratedNever();

        builder.Property(user => user.Username)
            .IsRequired()
            .HasMaxLength(UserConstraints.UsernameMaxLength)
            .UseCollation(CaseInsensitiveCollation);

        builder.Property(user => user.Email)
            .IsRequired()
            .HasMaxLength(UserConstraints.EmailMaxLength)
            .UseCollation(CaseInsensitiveCollation);

        builder.Property(user => user.FirstName)
            .IsRequired()
            .HasMaxLength(UserConstraints.NameMaxLength);

        builder.Property(user => user.LastName)
            .IsRequired()
            .HasMaxLength(UserConstraints.NameMaxLength);

        builder.Property(user => user.PasswordHash)
            .IsRequired()
            .HasMaxLength(UserConstraints.PasswordHashMaxLength);

        builder.Property(user => user.SecurityStamp)
            .IsRequired();

        builder.Property(user => user.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(user => user.DeletedBy)
            .HasMaxLength(UserConstraints.UsernameMaxLength);

        builder.Property(user => user.FailedLoginAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(user => user.CreatedBy)
            .IsRequired()
            .HasMaxLength(UserConstraints.UsernameMaxLength);

        builder.Property(user => user.LastModifiedBy)
            .HasMaxLength(UserConstraints.UsernameMaxLength);

        // Optimistic concurrency: SQL Server maintains this, EF adds it to the UPDATE predicate, and a stale
        // value matches zero rows - which surfaces as DbUpdateConcurrencyException instead of a lost update.
        builder.Property(user => user.RowVersion)
            .IsRowVersion();

        builder.HasOne(user => user.Role)
            .WithMany()
            .HasForeignKey(user => user.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(user => user.RefreshTokens)
            .WithOne(token => token.User)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // The collection is exposed as IReadOnlyCollection over a private field, so EF must write to the field.
        builder.Navigation(user => user.RefreshTokens)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        ConfigureIndexes(builder);

        // Deleted users are invisible by default. The only opt-out is IUserRepository.QueryIncludingDeleted(),
        // which has two justified callers; see docs/02-architecture.md section 7.
        builder.HasQueryFilter(user => !user.IsDeleted);
    }

    private static void ConfigureIndexes(EntityTypeBuilder<User> builder)
    {
        // Uniqueness is scoped to active users, so soft-deleting a user releases their username and email
        // (ADR-0009). Audit identity is the immutable UserId, so reuse cannot make history ambiguous.
        // Both indexes below cover the same column, so they must be declared with the naming overload of
        // HasIndex. Without an explicit name, EF treats a second HasIndex over the same property as a
        // reconfiguration of the first: the filtered unique index simply gets renamed and the second index is
        // never created. That failure is silent in the model and only visible in the produced DDL.
        builder.HasIndex(user => user.Username, "UQ_Users_ActiveUsername")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        // Sign-in must find soft-deleted rows in order to refuse them, and that query carries no IsDeleted
        // predicate - so it cannot use the filtered index above and needs an unfiltered one of its own.
        builder.HasIndex(user => user.Username, "IX_Users_Username_All");

        builder.HasIndex(user => user.Email, "UQ_Users_ActiveEmail")
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");

        builder.HasIndex(user => new { user.CreatedAt, user.Id })
            .IsClustered()
            .HasDatabaseName("CIX_Users_CreatedAt");

        // Covering indexes for the list projection: the page query becomes an index range scan with no key
        // lookups, which is what keeps search and paging cheap.
        builder.HasIndex(user => new { user.RoleId, user.IsDeleted })
            .IncludeProperties(user => new
            {
                user.Username,
                user.Email,
                user.FirstName,
                user.LastName,
                user.CreatedAt,
            })
            .HasDatabaseName("IX_Users_RoleId_IsDeleted");

        builder.HasIndex(user => user.IsDeleted)
            .IncludeProperties(user => new
            {
                user.Username,
                user.Email,
                user.FirstName,
                user.LastName,
                user.RoleId,
                user.CreatedAt,
            })
            .HasDatabaseName("IX_Users_IsDeleted");

        builder.HasIndex(user => new { user.LastName, user.FirstName })
            .HasDatabaseName("IX_Users_LastName_FirstName");
    }
}
