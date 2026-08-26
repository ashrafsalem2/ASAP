using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Locations;
using ASAP.Platform.Persistence;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory;

/// <summary>Registers the Inventory tables, under the <c>inv</c> schema.</summary>
public sealed partial class InventorySchema : IModuleSchema
{
    /// <inheritdoc />
    public string SchemaName => "inv";

    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<ItemCategory>(builder =>
        {
            builder.ToTable("ItemCategories", SchemaName);

            builder.Property(c => c.Code).HasMaxLength(32).IsRequired();
            builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
            builder.Property(c => c.NameArabic).HasMaxLength(200);
            builder.Property(c => c.InventoryAccountNo).HasMaxLength(20);
            builder.Property(c => c.CostOfGoodsSoldAccountNo).HasMaxLength(20);
            builder.Property(c => c.SalesAccountNo).HasMaxLength(20);
            builder.Property(c => c.VarianceAccountNo).HasMaxLength(20);
            builder.Property(c => c.RowVersion).IsRowVersion();

            builder.HasIndex(c => new { c.CompanyId, c.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<Item>(builder =>
        {
            builder.ToTable("Items", SchemaName);

            builder.Property(i => i.No).HasMaxLength(32).IsRequired();
            builder.Property(i => i.Description).HasMaxLength(250).IsRequired();
            builder.Property(i => i.DescriptionArabic).HasMaxLength(250);
            builder.Property(i => i.BaseUnitOfMeasure).HasMaxLength(16).IsRequired();
            builder.Property(i => i.Barcode).HasMaxLength(64);

            builder.Property(i => i.StandardCost).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(i => i.UnitCost).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(i => i.LastDirectCost).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(i => i.UnitPrice).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(i => i.QuantityOnHand).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(i => i.ReorderPoint).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(i => i.ReorderQuantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(i => i.RowVersion).IsRowVersion();

            builder.HasIndex(i => new { i.CompanyId, i.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // A till scans a barcode and needs the item in one seek, on every line of every sale.
            builder.HasIndex(i => new { i.CompanyId, i.Barcode })
                   .HasFilter("[Barcode] IS NOT NULL AND [IsDeleted] = 0");

            builder.HasOne(i => i.Category)
                   .WithMany()
                   .HasForeignKey(i => i.CategoryId)
                   .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Location>(builder =>
        {
            builder.ToTable("Locations", SchemaName);

            builder.Property(l => l.Code).HasMaxLength(32).IsRequired();
            builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
            builder.Property(l => l.NameArabic).HasMaxLength(200);
            builder.Property(l => l.Address).HasMaxLength(500);
            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.CompanyId, l.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<ItemLedgerEntry>(builder =>
        {
            builder.ToTable("ItemLedgerEntries", SchemaName);

            builder.Property(e => e.ItemNo).HasMaxLength(32).IsRequired();
            builder.Property(e => e.LocationCode).HasMaxLength(32).IsRequired();
            builder.Property(e => e.DocumentNo).HasMaxLength(64);
            builder.Property(e => e.SourceCode).HasMaxLength(32).IsRequired();
            builder.Property(e => e.SerialNo).HasMaxLength(64);
            builder.Property(e => e.LotNo).HasMaxLength(64);

            builder.Property(e => e.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(e => e.RemainingQuantity).HasColumnType(DecimalPrecisionConventions.Quantity);

            // Stock on hand for one item at one location, which the till asks on every line.
            builder.HasIndex(e => new { e.CompanyId, e.ItemId, e.LocationId, e.PostingDate });

            // The FIFO walk: receipts with stock left, oldest first. Filtered, so it stays small
            // however many million entries the table holds.
            builder.HasIndex(e => new { e.CompanyId, e.ItemId, e.LocationId, e.PostingDate, e.EntryType })
                   .HasFilter("[RemainingQuantity] > 0")
                   .HasDatabaseName("IX_ItemLedgerEntries_OpenLayers");

            // Movements that took stock which was not there, waiting for their cost to settle.
            builder.HasIndex(e => new { e.CompanyId, e.ItemId })
                   .HasFilter("[WentNegative] = 1")
                   .HasDatabaseName("IX_ItemLedgerEntries_WentNegative");

            builder.HasIndex(e => new { e.CompanyId, e.TransactionNo });

            builder.Ignore(e => e.IsInbound);
        });

        modelBuilder.Entity<ValueEntry>(builder =>
        {
            builder.ToTable("ValueEntries", SchemaName);

            builder.Property(v => v.ItemNo).HasMaxLength(32).IsRequired();
            builder.Property(v => v.DocumentNo).HasMaxLength(64);
            builder.Property(v => v.SourceCode).HasMaxLength(32).IsRequired();

            builder.Property(v => v.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(v => v.CostAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(v => v.UnitCost).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(v => v.SalesAmount).HasColumnType(DecimalPrecisionConventions.Money);

            builder.HasIndex(v => new { v.CompanyId, v.ItemId, v.PostingDate });
            builder.HasIndex(v => v.ItemLedgerEntryId);

            // Costs that have not reached the general ledger yet, which is what the posting run
            // claims. Filtered so it holds only outstanding work.
            builder.HasIndex(v => new { v.CompanyId, v.IsPostedToGl })
                   .HasFilter("[IsPostedToGl] = 0")
                   .HasDatabaseName("IX_ValueEntries_AwaitingGl");

            builder.HasOne(v => v.ItemLedgerEntry)
                   .WithMany()
                   .HasForeignKey(v => v.ItemLedgerEntryId)
                   .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ItemApplicationEntry>(builder =>
        {
            builder.ToTable("ItemApplications", SchemaName);

            builder.Property(a => a.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);

            builder.HasIndex(a => a.OutboundEntryId);
            builder.HasIndex(a => a.InboundEntryId);

            // Applications still waiting for a receipt to settle against. This is the work list
            // the cost adjustment routine walks.
            builder.HasIndex(a => new { a.CompanyId, a.ItemId })
                   .HasFilter("[IsOutstanding] = 1")
                   .HasDatabaseName("IX_ItemApplications_Outstanding");
        });

        ConfigureTransfers(modelBuilder);
    }
}
