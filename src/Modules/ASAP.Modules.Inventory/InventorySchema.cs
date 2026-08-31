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

        modelBuilder.Entity<UnitOfMeasure>(builder =>
        {
            builder.ToTable("UnitsOfMeasure", SchemaName);

            builder.Property(u => u.Code).HasMaxLength(10).IsRequired();
            builder.Property(u => u.Name).HasMaxLength(100).IsRequired();
            builder.Property(u => u.NameArabic).HasMaxLength(100);
            builder.Property(u => u.RowVersion).IsRowVersion();

            builder.HasIndex(u => new { u.CompanyId, u.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<ItemUnit>(builder =>
        {
            builder.ToTable("ItemUnits", SchemaName);

            builder.Property(u => u.UnitCode).HasMaxLength(10).IsRequired();
            builder.Property(u => u.Barcode).HasMaxLength(64);
            builder.Property(u => u.QuantityPerUnit).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(u => u.RowVersion).IsRowVersion();

            builder.HasIndex(u => new { u.ItemId, u.UnitCode })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // A till scans a case barcode and needs the item and the quantity in one seek, on
            // every line of every sale.
            builder.HasIndex(u => new { u.CompanyId, u.Barcode })
                   .HasFilter("[Barcode] IS NOT NULL AND [IsDeleted] = 0");

            builder.HasOne(u => u.Item)
                   .WithMany()
                   .HasForeignKey(u => u.ItemId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(u => u.IsBase);
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

        modelBuilder.Entity<ItemVariant>(builder =>
        {
            builder.ToTable("ItemVariants", SchemaName);

            builder.Property(v => v.Code).HasMaxLength(32).IsRequired();
            builder.Property(v => v.Description).HasMaxLength(250).IsRequired();
            builder.Property(v => v.DescriptionArabic).HasMaxLength(250);
            builder.Property(v => v.Barcode).HasMaxLength(64);
            builder.Property(v => v.LastDirectCost).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(v => v.RowVersion).IsRowVersion();

            builder.HasOne(v => v.Item)
                   .WithMany()
                   .HasForeignKey(v => v.ItemId)
                   .OnDelete(DeleteBehavior.Restrict);

            // Unique within its item, not the company: two items both having a RED is ordinary.
            builder.HasIndex(v => new { v.ItemId, v.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // A scan looks here first, like it does for units.
            builder.HasIndex(v => new { v.CompanyId, v.Barcode });
        });

        modelBuilder.Entity<Reservations.StockReservation>(builder =>

        {

            builder.ToTable("StockReservations", SchemaName);


            builder.Property(r => r.ItemNo).HasMaxLength(32).IsRequired();

            builder.Property(r => r.VariantCode).HasMaxLength(32);

            builder.Property(r => r.LocationCode).HasMaxLength(20).IsRequired();

            builder.Property(r => r.DocumentNo).HasMaxLength(20).IsRequired();

            builder.Property(r => r.SourceCode).HasMaxLength(20);

            builder.Property(r => r.ReleaseReason).HasMaxLength(500);

            builder.Property(r => r.Note).HasMaxLength(500);

            builder.Property(r => r.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);

            builder.Property(r => r.QuantityOutstanding).HasColumnType(DecimalPrecisionConventions.Quantity);

            builder.Property(r => r.RowVersion).IsRowVersion();


            // Every availability check sums along this one, on every line of every stock movement.

            builder.HasIndex(r => new { r.ItemId, r.VariantId, r.LocationId, r.QuantityOutstanding });


            // And releasing and consuming both start from the document.

            builder.HasIndex(r => new { r.CompanyId, r.DocumentNo });


            builder.Ignore(r => r.QuantityFulfilled);

            builder.Ignore(r => r.IsOutstanding);

        });


        modelBuilder.Entity<Reservations.StockReservation>(builder =>


        {


            builder.ToTable("StockReservations", SchemaName);



            builder.Property(r => r.ItemNo).HasMaxLength(32).IsRequired();


            builder.Property(r => r.VariantCode).HasMaxLength(32);


            builder.Property(r => r.LocationCode).HasMaxLength(20).IsRequired();


            builder.Property(r => r.DocumentNo).HasMaxLength(20).IsRequired();


            builder.Property(r => r.SourceCode).HasMaxLength(20);


            builder.Property(r => r.ReleaseReason).HasMaxLength(500);


            builder.Property(r => r.Note).HasMaxLength(500);


            builder.Property(r => r.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);


            builder.Property(r => r.QuantityOutstanding).HasColumnType(DecimalPrecisionConventions.Quantity);


            builder.Property(r => r.RowVersion).IsRowVersion();



            // Every availability check sums along this one, on every line of every stock movement.


            builder.HasIndex(r => new { r.ItemId, r.VariantId, r.LocationId, r.QuantityOutstanding });



            // And releasing and consuming both start from the document.


            builder.HasIndex(r => new { r.CompanyId, r.DocumentNo });



            builder.Ignore(r => r.QuantityFulfilled);


            builder.Ignore(r => r.IsOutstanding);


        });



        modelBuilder.Entity<Reservations.StockReservation>(builder =>



        {



            builder.ToTable("StockReservations", SchemaName);




            builder.Property(r => r.ItemNo).HasMaxLength(32).IsRequired();



            builder.Property(r => r.VariantCode).HasMaxLength(32);



            builder.Property(r => r.LocationCode).HasMaxLength(20).IsRequired();



            builder.Property(r => r.DocumentNo).HasMaxLength(20).IsRequired();



            builder.Property(r => r.SourceCode).HasMaxLength(20);



            builder.Property(r => r.ReleaseReason).HasMaxLength(500);



            builder.Property(r => r.Note).HasMaxLength(500);



            builder.Property(r => r.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);



            builder.Property(r => r.QuantityOutstanding).HasColumnType(DecimalPrecisionConventions.Quantity);



            builder.Property(r => r.RowVersion).IsRowVersion();




            // Every availability check sums along this one, on every line of every stock movement.



            builder.HasIndex(r => new { r.ItemId, r.VariantId, r.LocationId, r.QuantityOutstanding });




            // And releasing and consuming both start from the document.



            builder.HasIndex(r => new { r.CompanyId, r.DocumentNo });




            builder.Ignore(r => r.QuantityFulfilled);



            builder.Ignore(r => r.IsOutstanding);



        });




        modelBuilder.Entity<Adjustments.AdjustmentReason>(builder =>
        {
            builder.ToTable("AdjustmentReasons", SchemaName);

            builder.Property(r => r.Code).HasMaxLength(32).IsRequired();
            builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
            builder.Property(r => r.NameArabic).HasMaxLength(200);
            builder.Property(r => r.ContraAccountNo).HasMaxLength(20);
            builder.Property(r => r.RowVersion).IsRowVersion();

            builder.HasIndex(r => new { r.CompanyId, r.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<Bin>(builder =>
        {
            builder.ToTable("Bins", SchemaName);

            builder.Property(b => b.Code).HasMaxLength(32).IsRequired();
            builder.Property(b => b.Name).HasMaxLength(200);
            builder.Property(b => b.NameArabic).HasMaxLength(200);
            builder.Property(b => b.RowVersion).IsRowVersion();

            builder.HasOne(b => b.Location)
                   .WithMany()
                   .HasForeignKey(b => b.LocationId)
                   .OnDelete(DeleteBehavior.Restrict);

            // Unique within its location, not within the company: two warehouses both having an
            // A-01 is ordinary, and forcing them apart would put the warehouse name in every code
            // twice.
            builder.HasIndex(b => new { b.LocationId, b.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // The order a picker walks them in.
            builder.HasIndex(b => new { b.LocationId, b.PickOrder });
        });

        modelBuilder.Entity<ItemLedgerEntry>(builder =>
        {
            builder.ToTable("ItemLedgerEntries", SchemaName);

            builder.Property(e => e.ItemNo).HasMaxLength(32).IsRequired();
            builder.Property(e => e.LocationCode).HasMaxLength(32).IsRequired();
            builder.Property(e => e.BinCode).HasMaxLength(32);
            builder.Property(e => e.VariantCode).HasMaxLength(32);

            // The stock line's real identity once variants are in play. Every on-hand sum and
            // every cost layer query goes down this index.
            builder.HasIndex(e => new { e.ItemId, e.VariantId, e.LocationId, e.RemainingQuantity });

            builder.Property(e => e.ReasonCode).HasMaxLength(32);
            builder.Property(e => e.Note).HasMaxLength(500);

            // "What did we write off for breakage last quarter" reads this one.
            builder.HasIndex(e => new { e.CompanyId, e.ReasonCode, e.PostingDate });
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

            builder.HasIndex(e => new { e.ItemId, e.VariantId, e.IsOutstanding });

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
        ConfigureCounting(modelBuilder);
    }
}
