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

        modelBuilder.Entity<Approvals.PurchaseApprovalLimit>(builder =>

        {

            builder.ToTable("PurchaseApprovalLimits", SchemaName);


            builder.Property(l => l.UserName).HasMaxLength(100).IsRequired();

            builder.Property(l => l.DisplayName).HasMaxLength(200);

            builder.Property(l => l.MaximumAmount).HasColumnType(DecimalPrecisionConventions.Money);

            builder.Property(l => l.RowVersion).IsRowVersion();


            // One limit per person. Two would make "how much may they approve" depend on which

            // row was read first, which is not a question that may have two answers.

            builder.HasIndex(l => new { l.CompanyId, l.UserId })

                   .IsUnique()

                   .HasFilter("[IsDeleted] = 0");

        });


        modelBuilder.Entity<PurchaseOrder>(builder =>
        {
            builder.ToTable("PurchaseOrders", SchemaName);

            builder.Property(o => o.ApprovedByUserName).HasMaxLength(100);
            builder.Property(o => o.ApprovedAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(o => o.RejectionReason).HasMaxLength(500);
            builder.Ignore(o => o.TotalAmount);

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

            builder.Property(l => l.VariantCode).HasMaxLength(32);

            builder.Property(l => l.ItemNo).HasMaxLength(20);
            builder.Property(l => l.AccountNo).HasMaxLength(20);
            builder.Property(l => l.Description).HasMaxLength(250).IsRequired();
            builder.Property(l => l.LocationCode).HasMaxLength(20);
            builder.Property(l => l.TaxCode).HasMaxLength(20);

            builder.Property(l => l.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityReceived).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityInvoiced).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityReturned).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.DirectUnitCost).HasColumnType(DecimalPrecisionConventions.UnitAmount);

            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.PurchaseOrderId, l.LineNo }).IsUnique();

            builder.Ignore(l => l.LineAmount);
            builder.Ignore(l => l.OutstandingToReceive);
            builder.Ignore(l => l.ReceivedNotInvoiced);
            builder.Ignore(l => l.ReturnableQuantity);
        });
    }
}
