using ASAP.Modules.Pos.Receipts;
using ASAP.Modules.Pos.Sessions;
using ASAP.Modules.Pos.Stations;
using ASAP.Platform.Persistence;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos;

/// <summary>
/// Registers the point of sale tables.
/// </summary>
/// <remarks>
/// Everything lands in the <c>pos</c> schema, so it is obvious in the database which module owns
/// what once a dozen are installed.
/// </remarks>
public sealed class PosSchema : IModuleSchema
{
    /// <inheritdoc />
    public string SchemaName => "pos";

    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<PosStation>(builder =>
        {
            builder.ToTable("PosStations", SchemaName);

            builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
            builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
            builder.Property(s => s.NameArabic).HasMaxLength(200);
            builder.Property(s => s.LocationCode).HasMaxLength(20).IsRequired();
            builder.Property(s => s.DefaultCustomerNo).HasMaxLength(20).IsRequired();
            builder.Property(s => s.RowVersion).IsRowVersion();

            builder.HasIndex(s => new { s.CompanyId, s.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<PosSession>(builder =>
        {
            builder.ToTable("PosSessions", SchemaName);

            builder.Property(s => s.No).HasMaxLength(20).IsRequired();
            builder.Property(s => s.StationCode).HasMaxLength(20).IsRequired();
            builder.Property(s => s.CashierName).HasMaxLength(200);
            builder.Property(s => s.RowVersion).IsRowVersion();

            foreach (var money in new[]
                     {
                         nameof(PosSession.OpeningFloat),
                         nameof(PosSession.CashTendered),
                         nameof(PosSession.ChangeGiven),
                         nameof(PosSession.CashRefunded),
                         nameof(PosSession.CardTaken),
                         nameof(PosSession.OnAccountTaken),
                         nameof(PosSession.NetSales),
                         nameof(PosSession.TaxAmount),
                         nameof(PosSession.DeclaredCash),
                     })
            {
                builder.Property(money).HasColumnType(DecimalPrecisionConventions.Money);
            }

            builder.HasIndex(s => new { s.CompanyId, s.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // "Is this till open?" is asked before every single receipt, so it is a seek.
            builder.HasIndex(s => new { s.CompanyId, s.StationCode, s.Status });

            // "What did the shop take yesterday?" reads this one.
            builder.HasIndex(s => new { s.CompanyId, s.BusinessDate });

            builder.Ignore(s => s.ExpectedCash);
            builder.Ignore(s => s.Variance);
            builder.Ignore(s => s.GrossSales);
            builder.Ignore(s => s.IsOpen);
        });

        modelBuilder.Entity<PosReceipt>(builder =>
        {
            builder.ToTable("PosReceipts", SchemaName);

            builder.Property(r => r.No).HasMaxLength(20).IsRequired();
            builder.Property(r => r.StationCode).HasMaxLength(20).IsRequired();
            builder.Property(r => r.CustomerNo).HasMaxLength(20).IsRequired();
            builder.Property(r => r.CustomerName).HasMaxLength(200).IsRequired();
            builder.Property(r => r.LocationCode).HasMaxLength(20).IsRequired();
            builder.Property(r => r.ParkedAs).HasMaxLength(64);
            builder.Property(r => r.ReturnsReceiptNo).HasMaxLength(20);
            builder.Property(r => r.RowVersion).IsRowVersion();

            foreach (var money in new[]
                     {
                         nameof(PosReceipt.NetAmount),
                         nameof(PosReceipt.DiscountAmount),
                         nameof(PosReceipt.PromotionAmount),
                         nameof(PosReceipt.TaxAmount),
                         nameof(PosReceipt.RoundingAmount),
                         nameof(PosReceipt.CostAmount),
                         nameof(PosReceipt.ChangeGiven),
                     })
            {
                builder.Property(money).HasColumnType(DecimalPrecisionConventions.Money);
            }

            builder.HasIndex(r => new { r.CompanyId, r.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // Everything on this session, which is what a Z reading and the session screen read.
            builder.HasIndex(r => new { r.SessionId, r.Status });

            // Recalling a parked sale, and answering "what did this till take today?".
            builder.HasIndex(r => new { r.CompanyId, r.StationCode, r.BusinessDate });

            builder.HasOne(r => r.Session)
                   .WithMany()
                   .HasForeignKey(r => r.SessionId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(r => r.Lines)
                   .WithOne(l => l.PosReceipt!)
                   .HasForeignKey(l => l.PosReceiptId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(r => r.Tenders)
                   .WithOne(t => t.PosReceipt!)
                   .HasForeignKey(t => t.PosReceiptId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(r => r.TotalAmount);
            builder.Ignore(r => r.TenderedAmount);
            builder.Ignore(r => r.OutstandingAmount);
            builder.Ignore(r => r.IsEditable);
            builder.Ignore(r => r.IsReturn);
        });

        modelBuilder.Entity<PosReceiptLine>(builder =>
        {
            builder.ToTable("PosReceiptLines", SchemaName);

            builder.Property(l => l.ItemNo).HasMaxLength(20);
            builder.Property(l => l.AccountNo).HasMaxLength(20);
            builder.Property(l => l.Description).HasMaxLength(250).IsRequired();
            builder.Property(l => l.TaxCode).HasMaxLength(20);
            builder.Property(l => l.OfferCode).HasMaxLength(20);
            builder.Property(l => l.OfferDiscountAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(l => l.UnitCostAtSale).HasColumnType(DecimalPrecisionConventions.UnitAmount);

            // "What did this campaign cost us?" reads this one.
            builder.HasIndex(l => new { l.CompanyId, l.OfferCode });

            builder.Property(l => l.Quantity).HasColumnType(DecimalPrecisionConventions.Quantity);
            builder.Property(l => l.UnitPrice).HasColumnType(DecimalPrecisionConventions.UnitAmount);
            builder.Property(l => l.DiscountPercent).HasColumnType(DecimalPrecisionConventions.Percentage);

            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.PosReceiptId, l.LineNo }).IsUnique();

            builder.Ignore(l => l.NetUnitPrice);
            builder.Ignore(l => l.LineAmount);
            builder.Ignore(l => l.DiscountAmount);
        });

        modelBuilder.Entity<PosTender>(builder =>
        {
            builder.ToTable("PosTenders", SchemaName);

            builder.Property(t => t.Reference).HasMaxLength(64);
            builder.Property(t => t.Amount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(t => t.RowVersion).IsRowVersion();

            builder.HasIndex(t => new { t.PosReceiptId, t.LineNo }).IsUnique();
        });
    }
}
