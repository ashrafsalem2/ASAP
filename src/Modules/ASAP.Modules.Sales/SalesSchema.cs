using ASAP.Modules.Sales.Orders;
using ASAP.Platform.Persistence;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Sales;

/// <summary>
/// Registers the Sales tables.
/// </summary>
/// <remarks>
/// Everything lands in the <c>sal</c> schema, so it is obvious in the database which module owns
/// what once a dozen are installed.
/// </remarks>
public sealed class SalesSchema : IModuleSchema
{
    /// <inheritdoc />
    public string SchemaName => "sal";

    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Pricing.PriceList>(builder =>

        {

            builder.ToTable("PriceLists", SchemaName);


            builder.Property(l => l.Code).HasMaxLength(32).IsRequired();

            builder.Property(l => l.Name).HasMaxLength(200).IsRequired();

            builder.Property(l => l.NameArabic).HasMaxLength(200);

            builder.Property(l => l.RowVersion).IsRowVersion();


            builder.HasMany(l => l.Lines)

                   .WithOne(l => l.PriceList!)

                   .HasForeignKey(l => l.PriceListId)

                   .OnDelete(DeleteBehavior.Cascade);


            builder.HasIndex(l => new { l.CompanyId, l.Code })

                   .IsUnique()

                   .HasFilter("[IsDeleted] = 0");

        });


        modelBuilder.Entity<Pricing.PriceListLine>(builder =>

        {

            builder.ToTable("PriceListLines", SchemaName);


            builder.Property(l => l.ItemNo).HasMaxLength(32).IsRequired();

            builder.Property(l => l.VariantCode).HasMaxLength(32);

            builder.Property(l => l.UnitCode).HasMaxLength(10);

            builder.Property(l => l.MinimumQuantity).HasColumnType(DecimalPrecisionConventions.Quantity);

            builder.Property(l => l.UnitPrice).HasColumnType(DecimalPrecisionConventions.UnitAmount);

            builder.Property(l => l.DiscountPercent).HasColumnType(DecimalPrecisionConventions.Percentage);

            builder.Property(l => l.RowVersion).IsRowVersion();


            builder.Ignore(l => l.Specificity);


            // Every price lookup goes down this one.

            builder.HasIndex(l => new { l.PriceListId, l.ItemNo });

        });


        modelBuilder.Entity<Pricing.CustomerPriceList>(builder =>

        {

            builder.ToTable("CustomerPriceLists", SchemaName);


            builder.Property(c => c.CustomerNo).HasMaxLength(20).IsRequired();

            builder.Property(c => c.PriceListCode).HasMaxLength(32).IsRequired();

            builder.Property(c => c.RowVersion).IsRowVersion();


            // One list per customer. Two would make what they pay depend on which was read.

            builder.HasIndex(c => new { c.CompanyId, c.CustomerNo })

                   .IsUnique()

                   .HasFilter("[IsDeleted] = 0");

        });


        modelBuilder.Entity<SalesOrder>(builder =>
        {
            builder.ToTable("SalesOrders", SchemaName);

            builder.Property(o => o.No).HasMaxLength(20).IsRequired();
            builder.Property(o => o.CustomerNo).HasMaxLength(20).IsRequired();
            builder.Property(o => o.CustomerName).HasMaxLength(200).IsRequired();
            builder.Property(o => o.LocationCode).HasMaxLength(20);
            builder.Property(o => o.CustomerOrderNo).HasMaxLength(64);
            builder.Property(o => o.Description).HasMaxLength(250);
            builder.Property(o => o.RowVersion).IsRowVersion();

            builder.HasIndex(o => new { o.CompanyId, o.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // What this customer has on order, and what is still to go out. Both are seeks.
            builder.HasIndex(o => new { o.CompanyId, o.CustomerNo, o.OrderDate });
            builder.HasIndex(o => new { o.CompanyId, o.Status, o.OrderDate });

            builder.HasMany(o => o.Lines)
                   .WithOne(l => l.SalesOrder!)
                   .HasForeignKey(l => l.SalesOrderId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(o => o.IsEditable);
            builder.Ignore(o => o.HasOutstandingShipment);
            builder.Ignore(o => o.HasOutstandingInvoice);
        });

        modelBuilder.Entity<SalesOrderLine>(builder =>
        {
            builder.ToTable("SalesOrderLines", SchemaName);

            builder.Property(l => l.ItemNo).HasMaxLength(20);
            builder.Property(l => l.AccountNo).HasMaxLength(20);
            builder.Property(l => l.Description).HasMaxLength(250).IsRequired();
            builder.Property(l => l.LocationCode).HasMaxLength(20);
            builder.Property(l => l.TaxCode).HasMaxLength(20);

            builder.Property(l => l.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityShipped).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityInvoiced).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityReturned).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.UnitPrice).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(l => l.DiscountPercent).HasColumnType(DecimalPrecisionConventions.Percentage);

            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.SalesOrderId, l.LineNo }).IsUnique();

            builder.Ignore(l => l.NetUnitPrice);
            builder.Ignore(l => l.LineAmount);
            builder.Ignore(l => l.DiscountAmount);
            builder.Ignore(l => l.OutstandingToShip);
            builder.Ignore(l => l.ShippedNotInvoiced);
            builder.Ignore(l => l.ReturnableQuantity);
        });
    }
}
