using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Periods;
using ASAP.Platform.Persistence;
using ASAP.Platform.Persistence.Conventions;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance;

/// <summary>
/// Registers the Finance tables.
/// </summary>
/// <remarks>
/// Everything lands in the <c>fin</c> schema, so it is obvious in the database which module owns
/// what once a dozen modules are installed.
/// </remarks>
public sealed class FinanceSchema : IModuleSchema
{
    /// <inheritdoc />
    public string SchemaName => "fin";

    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        ConfigureAccounts(modelBuilder);
        ConfigurePeriods(modelBuilder);
        ConfigureJournals(modelBuilder);
        ConfigureLedger(modelBuilder);
    }

    private void ConfigureAccounts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GlAccount>(builder =>
        {
            builder.ToTable("GlAccounts", SchemaName);

            builder.Property(a => a.No).HasMaxLength(20).IsRequired();
            builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
            builder.Property(a => a.NameArabic).HasMaxLength(200);
            builder.Property(a => a.Totaling).HasMaxLength(500);
            builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsFixedLength();
            builder.Property(a => a.Balance).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(a => a.RowVersion).IsRowVersion();

            builder.HasIndex(a => new { a.CompanyId, a.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.Ignore(a => a.IsBalanceSheet);
            builder.Ignore(a => a.IsDebitAccount);
            builder.Ignore(a => a.IsPostable);
        });
    }

    private void ConfigurePeriods(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FiscalYear>(builder =>
        {
            builder.ToTable("FiscalYears", SchemaName);

            builder.Property(y => y.Code).HasMaxLength(20).IsRequired();
            builder.Property(y => y.RowVersion).IsRowVersion();

            builder.HasIndex(y => new { y.CompanyId, y.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // Every posting asks which year covers a date, so the range is indexed rather than scanned.
            builder.HasIndex(y => new { y.CompanyId, y.StartDate, y.EndDate });

            builder.HasMany(y => y.Periods)
                   .WithOne(p => p.FiscalYear!)
                   .HasForeignKey(p => p.FiscalYearId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FiscalPeriod>(builder =>
        {
            builder.ToTable("FiscalPeriods", SchemaName);

            builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
            builder.Property(p => p.NameArabic).HasMaxLength(100);
            builder.Property(p => p.RowVersion).IsRowVersion();

            builder.HasIndex(p => new { p.CompanyId, p.StartDate, p.EndDate });
            builder.HasIndex(p => new { p.FiscalYearId, p.PeriodNo }).IsUnique();
        });
    }

    private void ConfigureJournals(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GeneralJournalBatch>(builder =>
        {
            builder.ToTable("JournalBatches", SchemaName);

            builder.Property(b => b.Code).HasMaxLength(32).IsRequired();
            builder.Property(b => b.Description).HasMaxLength(200).IsRequired();
            builder.Property(b => b.NumberSeriesCode).HasMaxLength(64);
            builder.Property(b => b.SourceCode).HasMaxLength(32).IsRequired();
            builder.Property(b => b.RowVersion).IsRowVersion();

            builder.HasIndex(b => new { b.CompanyId, b.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.HasMany(b => b.Lines)
                   .WithOne(l => l.Batch!)
                   .HasForeignKey(l => l.BatchId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GeneralJournalLine>(builder =>
        {
            builder.ToTable("JournalLines", SchemaName);

            builder.Property(l => l.DocumentNo).HasMaxLength(64);
            builder.Property(l => l.Description).HasMaxLength(250);
            builder.Property(l => l.Amount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(l => l.CurrencyCode).HasMaxLength(3).IsFixedLength();
            builder.Property(l => l.ExchangeRate).HasColumnType(DecimalPrecisionConventions.ExchangeRate);
            builder.Property(l => l.ExternalDocumentNo).HasMaxLength(64);
            builder.Property(l => l.RowVersion).IsRowVersion();

            builder.HasIndex(l => new { l.BatchId, l.LineNo }).IsUnique();

            builder.Ignore(l => l.IsSelfBalancing);
        });
    }

    private void ConfigureLedger(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GlEntry>(builder =>
        {
            builder.ToTable("GlEntries", SchemaName);

            builder.Property(e => e.AccountNo).HasMaxLength(20).IsRequired();
            builder.Property(e => e.DocumentNo).HasMaxLength(64);
            builder.Property(e => e.Description).HasMaxLength(250).IsRequired();
            builder.Property(e => e.SourceCode).HasMaxLength(32).IsRequired();
            builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsFixedLength();

            builder.Property(e => e.Amount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.DebitAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.CreditAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.AmountInCurrency).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.ExchangeRate).HasColumnType(DecimalPrecisionConventions.ExchangeRate);

            // The three questions asked of a ledger, in the order they are asked: what is on this
            // account in this period, what made up this transaction, and where did this document
            // land. Each is a seek rather than a scan of a table that will hold millions of rows.
            builder.HasIndex(e => new { e.CompanyId, e.AccountId, e.PostingDate });
            builder.HasIndex(e => new { e.CompanyId, e.TransactionNo });
            builder.HasIndex(e => new { e.CompanyId, e.DocumentNo })
                   .HasFilter("[DocumentNo] IS NOT NULL");

            // Filtering by department is only cheap if the shortcut value is on the entry itself.
            builder.HasIndex(e => new { e.CompanyId, e.ShortcutDimension1Id, e.PostingDate })
                   .HasFilter("[ShortcutDimension1Id] IS NOT NULL");

            builder.HasOne<GlAccount>()
                   .WithMany()
                   .HasForeignKey(e => e.AccountId)
                   .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TransactionCounter>(builder =>
        {
            builder.ToTable("TransactionCounters", SchemaName);

            // One row per company, and the allocator finds it by company rather than by key.
            builder.HasIndex(c => c.CompanyId).IsUnique();
        });
    }
}
