using ASAP.Modules.Promotions.Offers;
using ASAP.Platform.Persistence;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Promotions;

/// <summary>
/// Registers the Promotions tables.
/// </summary>
/// <remarks>
/// Everything lands in the <c>prm</c> schema, so it is obvious in the database which module owns
/// what once a dozen are installed.
/// </remarks>
public sealed class PromotionsSchema : IModuleSchema
{
    /// <inheritdoc />
    public string SchemaName => "prm";

    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Offer>(builder =>
        {
            builder.ToTable("Offers", SchemaName);

            builder.Property(o => o.Code).HasMaxLength(20).IsRequired();
            builder.Property(o => o.Name).HasMaxLength(200).IsRequired();
            builder.Property(o => o.NameArabic).HasMaxLength(200);
            builder.Property(o => o.CustomerGroup).HasMaxLength(40);
            builder.Property(o => o.CouponCode).HasMaxLength(40);

            builder.Property(o => o.Value).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(o => o.BuyQuantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(o => o.GetQuantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(o => o.GetDiscountPercent).HasColumnType(DecimalPrecisionConventions.Percentage);

            builder.HasIndex(o => new { o.CompanyId, o.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // "What is running today?" is asked before every basket, so it is a seek.
            builder.HasIndex(o => new { o.CompanyId, o.IsActive, o.StartsOn, o.EndsOn });

            builder.HasMany(o => o.Targets)
                   .WithOne(t => t.Offer!)
                   .HasForeignKey(t => t.OfferId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OfferTarget>(builder =>
        {
            builder.ToTable("OfferTargets", SchemaName);

            builder.Property(t => t.ItemNo).HasMaxLength(20);

            builder.HasIndex(t => new { t.OfferId, t.ItemNo });
            builder.HasIndex(t => new { t.OfferId, t.CategoryId });
        });
    }
}
