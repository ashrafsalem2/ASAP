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

        modelBuilder.Entity<Quotes.SalesQuote>(builder =>

        {

            builder.ToTable("SalesQuotes", SchemaName);


            builder.Property(q => q.No).HasMaxLength(20).IsRequired();

            builder.Property(q => q.CustomerNo).HasMaxLength(20).IsRequired();

            builder.Property(q => q.CustomerName).HasMaxLength(200).IsRequired();

            builder.Property(q => q.LocationCode).HasMaxLength(20);

            builder.Property(q => q.CustomerOrderNo).HasMaxLength(50);

            builder.Property(q => q.Description).HasMaxLength(500);

            builder.Property(q => q.OrderNo).HasMaxLength(20);

            builder.Property(q => q.DeclineReason).HasMaxLength(500);

            builder.Property(q => q.Status).HasConversion<int>();

            builder.Property(q => q.RowVersion).IsRowVersion();


            builder.HasMany(q => q.Lines)

                   .WithOne(l => l.SalesQuote!)

                   .HasForeignKey(l => l.SalesQuoteId)

                   .OnDelete(DeleteBehavior.Cascade);


            builder.HasIndex(q => new { q.CompanyId, q.No })

                   .IsUnique()

                   .HasFilter("[IsDeleted] = 0");


            // The expiry sweep reads this one, and so does anybody asking what is still outstanding.

            builder.HasIndex(q => new { q.CompanyId, q.Status, q.ValidUntil });


            builder.Ignore(q => q.TotalAmount);

            builder.Ignore(q => q.IsEditable);

        });


        modelBuilder.Entity<Quotes.SalesQuoteLine>(builder =>

        {

            builder.ToTable("SalesQuoteLines", SchemaName);


            builder.Property(l => l.ItemNo).HasMaxLength(32);

            builder.Property(l => l.VariantCode).HasMaxLength(32);

            builder.Property(l => l.AccountNo).HasMaxLength(20);

            builder.Property(l => l.Description).HasMaxLength(500).IsRequired();

            builder.Property(l => l.LocationCode).HasMaxLength(20);

            builder.Property(l => l.TaxCode).HasMaxLength(20);

            builder.Property(l => l.Type).HasConversion<int>();

            builder.Property(l => l.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);

            builder.Property(l => l.UnitPrice).HasColumnType(DecimalPrecisionConventions.UnitAmount);

            builder.Property(l => l.DiscountPercent).HasColumnType(DecimalPrecisionConventions.Percentage);

            builder.Property(l => l.RowVersion).IsRowVersion();


            builder.HasIndex(l => new { l.SalesQuoteId, l.LineNo }).IsUnique();


            builder.Ignore(l => l.NetUnitPrice);

            builder.Ignore(l => l.LineAmount);

        });


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


        modelBuilder.Entity<Pricing.CustomerGroupPriceList>(builder =>


        {


            builder.ToTable("CustomerGroupPriceLists", SchemaName);



            builder.Property(g => g.CustomerGroupCode).HasMaxLength(40).IsRequired();


            builder.Property(g => g.PriceListCode).HasMaxLength(32).IsRequired();


            builder.Property(g => g.RowVersion).IsRowVersion();



            // One list per group, for the same reason as one per customer: two would make what a


            // whole class of customer pays depend on which row was read.


            builder.HasIndex(g => new { g.CompanyId, g.CustomerGroupCode })


                   .IsUnique()


                   .HasFilter("[IsDeleted] = 0");


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
