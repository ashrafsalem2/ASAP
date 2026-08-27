using ASAP.Modules.Purchasing.Orders;
using ASAP.Platform.Persistence;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Purchasing;

/// <summary>
/// Registers the Purchasing tables.
/// </summary>
/// <remarks>
/// Everything lands in the <c>pur</c> schema, so it is obvious in the database which module owns
/// what once a dozen are installed.
/// </remarks>
public sealed class PurchasingSchema : IModuleSchema
{
    /// <inheritdoc />
    public string SchemaName => "pur";

    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PurchaseOrder>(builder =>
        {
            builder.ToTable("PurchaseOrders", SchemaName);

            builder.Property(o => o.No).HasMaxLength(20).IsRequired();
            builder.Property(o => o.VendorNo).HasMaxLength(20).IsRequired();
            builder.Property(o => o.VendorName).HasMaxLength(200).IsRequired();
            builder.Property(o => o.LocationCode).HasMaxLength(20);
            builder.Property(o => o.VendorOrderNo).HasMaxLength(64);
            builder.Property(o => o.Description).HasMaxLength(250);
            builder.Property(o => o.RowVersion).IsRowVersion();

            builder.HasIndex(o => new { o.CompanyId, o.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // The two lists anybody opens: what is on order with this vendor, and what is still
            // outstanding. Both are seeks rather than scans.
            builder.HasIndex(o => new { o.CompanyId, o.VendorNo, o.OrderDate });
            builder.HasIndex(o => new { o.CompanyId, o.Status, o.OrderDate });

            builder.HasMany(o => o.Lines)
                   .WithOne(l => l.PurchaseOrder!)
                   .HasForeignKey(l => l.PurchaseOrderId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(o => o.IsEditable);
            builder.Ignore(o => o.HasOutstandingReceipt);
            builder.Ignore(o => o.HasOutstandingInvoice);
        });

        modelBuilder.Entity<PurchaseOrderLine>(builder =>
        {
            builder.ToTable("PurchaseOrderLines", SchemaName);

            builder.Property(l => l.ItemNo).HasMaxLength(20);
            builder.Property(l => l.AccountNo).HasMaxLength(20);
            builder.Property(l => l.Description).HasMaxLength(250).IsRequired();
            builder.Property(l => l.LocationCode).HasMaxLength(20);
            builder.Property(l => l.TaxCode).HasMaxLength(20);

            builder.Property(l => l.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityReceived).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityInvoiced).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.DirectUnitCost).HasColumnType(DecimalPrecisionConventions.UnitAmount);

            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.PurchaseOrderId, l.LineNo }).IsUnique();

            builder.Ignore(l => l.LineAmount);
            builder.Ignore(l => l.OutstandingToReceive);
            builder.Ignore(l => l.ReceivedNotInvoiced);
        });
    }
}
