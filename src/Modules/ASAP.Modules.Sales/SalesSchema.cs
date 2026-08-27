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
            builder.Property(l => l.UnitPrice).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(l => l.DiscountPercent).HasColumnType(DecimalPrecisionConventions.Percentage);

            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.SalesOrderId, l.LineNo }).IsUnique();

            builder.Ignore(l => l.NetUnitPrice);
            builder.Ignore(l => l.LineAmount);
            builder.Ignore(l => l.DiscountAmount);
            builder.Ignore(l => l.OutstandingToShip);
            builder.Ignore(l => l.ShippedNotInvoiced);
        });
    }
}
