using ASAP.Platform.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ASAP.Platform.Persistence.Configurations;

/// <summary>Maps <see cref="Tenant"/>.</summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Tenants");

        builder.Property(t => t.Code).HasMaxLength(32).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.NameArabic).HasMaxLength(200);
        builder.Property(t => t.DefaultCulture).HasMaxLength(16).IsRequired();
        builder.Property(t => t.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        // A short list that is read on every licence check and never queried across. Storing it
        // as JSON keeps it beside the tenant instead of costing a join for two dozen strings.
        builder.Property(t => t.LicensedModules)
               .HasColumnType("nvarchar(max)")
               .HasConversion(
                   v => string.Join(',', v),
                   v => v.Length == 0
                       ? new List<string>()
                       : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

        builder.HasIndex(t => t.Code).IsUnique();

        builder.HasMany(t => t.Companies)
               .WithOne(c => c.Tenant!)
               .HasForeignKey(c => c.TenantId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="Company"/>.</summary>
public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Companies");

        builder.Property(c => c.Code).HasMaxLength(32).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.NameArabic).HasMaxLength(200);
        builder.Property(c => c.RegistrationNo).HasMaxLength(64);
        builder.Property(c => c.TaxRegistrationNo).HasMaxLength(64);
        builder.Property(c => c.BaseCurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(c => c.RowVersion).IsRowVersion();

        builder.HasIndex(c => new { c.TenantId, c.Code }).IsUnique();

        builder.HasMany(c => c.Branches)
               .WithOne()
               .HasForeignKey(b => b.CompanyId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Maps <see cref="Branch"/>.</summary>
public sealed class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Branches");

        builder.Property(b => b.Code).HasMaxLength(32).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.NameArabic).HasMaxLength(200);
        builder.Property(b => b.Address).HasMaxLength(500);
        builder.Property(b => b.City).HasMaxLength(100);
        builder.Property(b => b.Phone).HasMaxLength(32);
        builder.Property(b => b.TimeZoneId).HasMaxLength(64);
        builder.Property(b => b.RowVersion).IsRowVersion();

        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();
    }
}
