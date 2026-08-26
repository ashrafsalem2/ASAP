using ASAP.Platform.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ASAP.Platform.Persistence.Configurations;

/// <summary>Maps <see cref="User"/>.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users");

        builder.Property(u => u.UserName).HasMaxLength(128).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.Phone).HasMaxLength(32);
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(u => u.Culture).HasMaxLength(16);
        builder.Property(u => u.RowVersion).IsRowVersion();

        // Login names are compared without regard to case, so uniqueness must be too. The index
        // is filtered on IsDeleted: retiring a user has to free their name for reuse, or a
        // returning employee cannot be given back their own login.
        builder.HasIndex(u => new { u.TenantId, u.UserName })
               .IsUnique()
               .HasFilter("[IsDeleted] = 0");

        builder.HasMany(u => u.Assignments)
               .WithOne(a => a.User!)
               .HasForeignKey(a => a.UserId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PermissionSet"/>.</summary>
public sealed class PermissionSetConfiguration : IEntityTypeConfiguration<PermissionSet>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PermissionSet> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PermissionSets");

        builder.Property(p => p.Code).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.NameArabic).HasMaxLength(200);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => new { p.TenantId, p.Code })
               .IsUnique()
               .HasFilter("[IsDeleted] = 0");

        builder.HasMany(p => p.Entries)
               .WithOne(e => e.PermissionSet!)
               .HasForeignKey(e => e.PermissionSetId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Includes)
               .WithOne(i => i.PermissionSet!)
               .HasForeignKey(i => i.PermissionSetId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Maps <see cref="PermissionSetEntry"/>.</summary>
public sealed class PermissionSetEntryConfiguration : IEntityTypeConfiguration<PermissionSetEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PermissionSetEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PermissionSetEntries");

        // Text rather than a foreign key, so a set keeps referring to a permission whose
        // extension happens to be uninstalled, and starts working again when it returns.
        builder.Property(e => e.PermissionKey).HasMaxLength(128).IsRequired();

        builder.HasIndex(e => new { e.PermissionSetId, e.PermissionKey }).IsUnique();
    }
}

/// <summary>Maps <see cref="PermissionSetInclusion"/>.</summary>
public sealed class PermissionSetInclusionConfiguration : IEntityTypeConfiguration<PermissionSetInclusion>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PermissionSetInclusion> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("PermissionSetInclusions");

        builder.HasIndex(i => new { i.PermissionSetId, i.IncludedPermissionSetId }).IsUnique();

        // Restrict, not cascade: deleting a set that another includes would silently strip
        // permissions from everyone holding the including set. Make the administrator unpick it.
        builder.HasOne(i => i.IncludedPermissionSet!)
               .WithMany()
               .HasForeignKey(i => i.IncludedPermissionSetId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="UserPermissionAssignment"/>.</summary>
public sealed class UserPermissionAssignmentConfiguration : IEntityTypeConfiguration<UserPermissionAssignment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserPermissionAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("UserPermissionAssignments");

        builder.HasIndex(a => new { a.UserId, a.CompanyId });

        builder.HasOne(a => a.PermissionSet!)
               .WithMany()
               .HasForeignKey(a => a.PermissionSetId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}
