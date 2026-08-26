using ASAP.Modules.Inventory.Transfers;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory;

/// <summary>Registers the transfer tables.</summary>
public sealed partial class InventorySchema
{
    private void ConfigureTransfers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransferOrder>(builder =>
        {
            builder.ToTable("TransferOrders", SchemaName);

            builder.Property(t => t.No).HasMaxLength(32).IsRequired();
            builder.Property(t => t.FromLocationCode).HasMaxLength(32).IsRequired();
            builder.Property(t => t.ToLocationCode).HasMaxLength(32).IsRequired();
            builder.Property(t => t.Description).HasMaxLength(500);
            builder.Property(t => t.RowVersion).IsRowVersion();

            builder.HasIndex(t => new { t.CompanyId, t.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // The question a branch actually asks: what is coming to me, and what have I not sent
            // yet. Filtered to the transfers still in play, so it stays small as history grows.
            builder.HasIndex(t => new { t.CompanyId, t.Status, t.ToLocationId })
                   .HasFilter("[Status] < 4 AND [IsDeleted] = 0")
                   .HasDatabaseName("IX_TransferOrders_InPlay");

            builder.HasMany(t => t.Lines)
                   .WithOne(l => l.TransferOrder!)
                   .HasForeignKey(l => l.TransferOrderId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(t => t.IsEditable);
            builder.Ignore(t => t.HasShipped);
        });

        modelBuilder.Entity<TransferOrderLine>(builder =>
        {
            builder.ToTable("TransferOrderLines", SchemaName);

            builder.Property(l => l.ItemNo).HasMaxLength(32).IsRequired();
            builder.Property(l => l.Description).HasMaxLength(250).IsRequired();
            builder.Property(l => l.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityShipped).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.QuantityReceived).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.TransferOrderId, l.LineNo }).IsUnique();

            builder.Ignore(l => l.OutstandingToShip);
            builder.Ignore(l => l.InTransit);
        });
    }
}
