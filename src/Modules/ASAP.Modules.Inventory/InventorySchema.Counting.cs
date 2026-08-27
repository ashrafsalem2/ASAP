using ASAP.Modules.Inventory.Counting;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory;

/// <summary>Registers the stock count tables.</summary>
public sealed partial class InventorySchema
{
    private void ConfigureCounting(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StockCount>(builder =>
        {
            builder.ToTable("StockCounts", SchemaName);

            builder.Property(c => c.No).HasMaxLength(32).IsRequired();
            builder.Property(c => c.LocationCode).HasMaxLength(32).IsRequired();
            builder.Property(c => c.Description).HasMaxLength(500);
            builder.Property(c => c.RowVersion).IsRowVersion();

            builder.HasIndex(c => new { c.CompanyId, c.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // "Is anybody counting here?" is asked before every count is started, and the answer
            // has to be quick even when the history is years deep.
            builder.HasIndex(c => new { c.CompanyId, c.LocationCode, c.Status })
                   .HasFilter("[Status] = 0 AND [IsDeleted] = 0")
                   .HasDatabaseName("IX_StockCounts_Open");

            builder.HasMany(c => c.Lines)
                   .WithOne(l => l.StockCount!)
                   .HasForeignKey(l => l.StockCountId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(c => c.IsEditable);
            builder.Ignore(c => c.Counted);
            builder.Ignore(c => c.NotCounted);
            builder.Ignore(c => c.Differences);
        });

        modelBuilder.Entity<StockCountLine>(builder =>
        {
            builder.ToTable("StockCountLines", SchemaName);

            builder.Property(l => l.ItemNo).HasMaxLength(32).IsRequired();
            builder.Property(l => l.Description).HasMaxLength(250).IsRequired();
            builder.Property(l => l.Note).HasMaxLength(500);
            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.StockCountId, l.ItemNo }).IsUnique();

            builder.Ignore(l => l.Difference);
        });
    }
}
