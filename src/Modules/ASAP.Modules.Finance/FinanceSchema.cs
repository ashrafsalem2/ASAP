using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Banking;
using ASAP.Modules.Finance.Currencies;
using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Periods;
using ASAP.Modules.Finance.Reporting;
using ASAP.Modules.Finance.Tax;
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
        ConfigureParties(modelBuilder);
        ConfigureTax(modelBuilder);
    }

    private void ConfigureTax(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountSchedule>(builder =>
        {
            builder.ToTable("AccountSchedules", SchemaName);

            builder.Property(s => s.Code).HasMaxLength(20).IsRequired();
            builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
            builder.Property(s => s.NameArabic).HasMaxLength(200);
            builder.Property(s => s.Description).HasMaxLength(500);
            builder.Property(s => s.RowVersion).IsRowVersion();

            builder.HasIndex(s => new { s.CompanyId, s.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.HasMany(s => s.Lines)
                   .WithOne(l => l.AccountSchedule!)
                   .HasForeignKey(l => l.AccountScheduleId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccountScheduleLine>(builder =>
        {
            builder.ToTable("AccountScheduleLines", SchemaName);

            builder.Property(l => l.RowNo).HasMaxLength(20).IsRequired();
            builder.Property(l => l.Description).HasMaxLength(200).IsRequired();
            builder.Property(l => l.DescriptionArabic).HasMaxLength(200);
            builder.Property(l => l.Expression).HasMaxLength(500);
            builder.Property(l => l.RowVersion).IsRowVersion();

            // A formula names a row, so two rows on one name is a formula with two answers.
            builder.HasIndex(l => new { l.AccountScheduleId, l.RowNo }).IsUnique();
        });

        modelBuilder.Entity<BankAccount>(builder =>
        {
            builder.ToTable("BankAccounts", SchemaName);

            builder.Property(a => a.Code).HasMaxLength(20).IsRequired();
            builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
            builder.Property(a => a.NameArabic).HasMaxLength(200);
            builder.Property(a => a.BankName).HasMaxLength(200);
            builder.Property(a => a.AccountNo).HasMaxLength(64);
            builder.Property(a => a.Iban).HasMaxLength(34);
            builder.Property(a => a.GlAccountNo).HasMaxLength(20).IsRequired();
            builder.Property(a => a.CurrencyCode).HasMaxLength(3).IsFixedLength();
            builder.Property(a => a.RowVersion).IsRowVersion();

            builder.HasIndex(a => new { a.CompanyId, a.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            // One ledger account per bank account. Two banks sharing one cannot be reconciled
            // against either statement, because every unmatched entry might belong to the other.
            builder.HasIndex(a => new { a.CompanyId, a.GlAccountNo })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");
        });

        modelBuilder.Entity<BankStatement>(builder =>
        {
            builder.ToTable("BankStatements", SchemaName);

            builder.Property(s => s.No).HasMaxLength(40).IsRequired();
            builder.Property(s => s.OpeningBalance).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(s => s.ClosingBalance).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(s => s.RowVersion).IsRowVersion();

            builder.HasIndex(s => new { s.CompanyId, s.BankAccountId, s.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.HasIndex(s => new { s.CompanyId, s.BankAccountId, s.StatementDate });

            builder.HasOne(s => s.BankAccount)
                   .WithMany()
                   .HasForeignKey(s => s.BankAccountId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(s => s.Lines)
                   .WithOne(l => l.BankStatement!)
                   .HasForeignKey(l => l.BankStatementId)
                   .OnDelete(DeleteBehavior.Cascade);

            builder.Ignore(s => s.StatementMovement);
            builder.Ignore(s => s.LineTotal);
            builder.Ignore(s => s.IsEditable);
        });

        modelBuilder.Entity<BankStatementLine>(builder =>
        {
            builder.ToTable("BankStatementLines", SchemaName);

            builder.Property(l => l.Description).HasMaxLength(250).IsRequired();
            builder.Property(l => l.Reference).HasMaxLength(64);
            builder.Property(l => l.Note).HasMaxLength(250);
            builder.Property(l => l.Amount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(l => l.RowVersion).IsRowVersion();

            // "Has this entry been reconciled" is asked of every entry on the account at every
            // reconciliation, and it is asked from this side because the ledger is never written.
            builder.HasIndex(l => new { l.CompanyId, l.MatchedEntryId })
                   .HasFilter("[MatchedEntryId] IS NOT NULL");

            builder.Ignore(l => l.IsMatched);
        });

        modelBuilder.Entity<Currency>(builder =>
        {
            builder.ToTable("Currencies", SchemaName);

            builder.Property(c => c.Code).HasMaxLength(3).IsFixedLength().IsRequired();
            builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
            builder.Property(c => c.NameArabic).HasMaxLength(100);
            builder.Property(c => c.Symbol).HasMaxLength(8);
            builder.Property(c => c.RowVersion).IsRowVersion();

            builder.HasIndex(c => new { c.CompanyId, c.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.HasMany(c => c.Rates)
                   .WithOne(r => r.Currency!)
                   .HasForeignKey(r => r.CurrencyId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExchangeRate>(builder =>
        {
            builder.ToTable("ExchangeRates", SchemaName);

            builder.Property(r => r.CurrencyAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(r => r.BaseAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(r => r.RowVersion).IsRowVersion();

            // Every posting in a foreign currency asks which rate was in force on a date, so the
            // lookup is a seek. Unique for the same reason a tax rate is: two rates starting on
            // one day is not a conflict anybody can resolve, so it is refused at the table.
            builder.HasIndex(r => new { r.CurrencyId, r.StartingDate }).IsUnique();

            builder.Ignore(r => r.IsUsable);
            builder.Ignore(r => r.Multiplier);
        });

        modelBuilder.Entity<TaxCode>(builder =>
        {
            builder.ToTable("TaxCodes", SchemaName);

            builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
            builder.Property(c => c.Description).HasMaxLength(200).IsRequired();
            builder.Property(c => c.DescriptionArabic).HasMaxLength(200);
            builder.Property(c => c.OutputAccountNo).HasMaxLength(20);
            builder.Property(c => c.InputAccountNo).HasMaxLength(20);
            builder.Property(c => c.RowVersion).IsRowVersion();

            builder.HasIndex(c => new { c.CompanyId, c.Code })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.HasMany(c => c.Rates)
                   .WithOne(r => r.TaxCode!)
                   .HasForeignKey(r => r.TaxCodeId)
                   .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaxRate>(builder =>
        {
            builder.ToTable("TaxRates", SchemaName);

            builder.Property(r => r.Percentage).HasColumnType(DecimalPrecisionConventions.Percentage);
            builder.Property(r => r.RowVersion).IsRowVersion();

            // Every posting asks which rate was in force on a date, so the lookup is a seek.
            builder.HasIndex(r => new { r.TaxCodeId, r.StartingDate }).IsUnique();
        });

        modelBuilder.Entity<TaxEntry>(builder =>
        {
            builder.ToTable("TaxEntries", SchemaName);

            builder.Property(e => e.TaxCodeNo).HasMaxLength(20).IsRequired();
            builder.Property(e => e.DocumentNo).HasMaxLength(64);
            builder.Property(e => e.ExternalDocumentNo).HasMaxLength(64);
            builder.Property(e => e.PartyNo).HasMaxLength(20);
            builder.Property(e => e.PartyName).HasMaxLength(200);
            builder.Property(e => e.PartyTaxRegistrationNo).HasMaxLength(40);
            builder.Property(e => e.TaxAccountNo).HasMaxLength(20);
            builder.Property(e => e.SourceCode).HasMaxLength(32).IsRequired();

            builder.Property(e => e.Percentage).HasColumnType(DecimalPrecisionConventions.Percentage);
            builder.Property(e => e.BaseAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.TaxAmount).HasColumnType(DecimalPrecisionConventions.Money);

            // The question a return asks: everything in this period, not yet declared, grouped by
            // code and direction. Filtered to the open entries, because a ledger is mostly closed
            // periods within a year of going live.
            builder.HasIndex(e => new { e.CompanyId, e.PostingDate, e.Direction })
                   .HasFilter("[IsClosed] = 0");

            builder.HasIndex(e => new { e.CompanyId, e.TransactionNo });

            builder.HasOne<TaxCode>()
                   .WithMany()
                   .HasForeignKey(e => e.TaxCodeId)
                   .OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Registers the customer and vendor ledgers.
    /// </summary>
    /// <remarks>
    /// Customers and vendors share a base class but not a table. The base is never registered as
    /// an entity type, so EF maps each concrete type as its own root rather than inventing a
    /// discriminator over a hierarchy that has no reason to share storage.
    /// </remarks>
    private void ConfigureParties(ModelBuilder modelBuilder)
    {
        ConfigureParty<Customer>(modelBuilder, "Customers");
        ConfigureParty<Vendor>(modelBuilder, "Vendors");

        ConfigurePartyLedger<CustomerLedgerEntry, Customer>(modelBuilder, "CustomerLedgerEntries");
        ConfigurePartyLedger<VendorLedgerEntry, Vendor>(modelBuilder, "VendorLedgerEntries");

        ConfigureApplication<CustomerApplication, CustomerLedgerEntry>(modelBuilder, "CustomerApplications");
        ConfigureApplication<VendorApplication, VendorLedgerEntry>(modelBuilder, "VendorApplications");
    }

    private void ConfigureParty<TParty>(ModelBuilder modelBuilder, string table)
        where TParty : Party
        => modelBuilder.Entity<TParty>(builder =>
        {
            builder.ToTable(table, SchemaName);

            builder.Property(p => p.No).HasMaxLength(20).IsRequired();
            builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
            builder.Property(p => p.NameArabic).HasMaxLength(200);
            builder.Property(p => p.ControlAccountNo).HasMaxLength(20);
            builder.Property(p => p.Email).HasMaxLength(320);
            builder.Property(p => p.Phone).HasMaxLength(40);
            builder.Property(p => p.TaxRegistrationNo).HasMaxLength(40);
            builder.Property(p => p.CreditLimit).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(p => p.Balance).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(p => p.RowVersion).IsRowVersion();

            builder.HasIndex(p => new { p.CompanyId, p.No })
                   .IsUnique()
                   .HasFilter("[IsDeleted] = 0");

            builder.Ignore(p => p.Kind);
            builder.Ignore(p => p.IsPostable);
        });

    private void ConfigurePartyLedger<TEntry, TParty>(ModelBuilder modelBuilder, string table)
        where TEntry : PartyLedgerEntry
        where TParty : Party
        => modelBuilder.Entity<TEntry>(builder =>
        {
            builder.ToTable(table, SchemaName);

            builder.Property(e => e.PartyNo).HasMaxLength(20).IsRequired();
            builder.Property(e => e.PartyName).HasMaxLength(200).IsRequired();
            builder.Property(e => e.DocumentNo).HasMaxLength(64);
            builder.Property(e => e.ExternalDocumentNo).HasMaxLength(64);
            builder.Property(e => e.Description).HasMaxLength(250).IsRequired();
            builder.Property(e => e.ControlAccountNo).HasMaxLength(20).IsRequired();
            builder.Property(e => e.SourceCode).HasMaxLength(32).IsRequired();
            builder.Property(e => e.CurrencyCode).HasMaxLength(3).IsFixedLength();

            builder.Property(e => e.Amount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.RemainingAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.AmountInCurrency).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(e => e.RemainingAmountInCurrency)
                   .HasColumnType(DecimalPrecisionConventions.Money);

            // What is still owed, which is what the aged analysis, the statement and the
            // application screen all ask for. Filtered so the index covers only the rows anybody
            // looks for; a ledger is mostly closed entries within a year of going live.
            builder.HasIndex(e => new { e.CompanyId, e.PartyId, e.DueDate })
                   .HasFilter("[IsOpen] = 1");

            builder.HasIndex(e => new { e.CompanyId, e.PartyId, e.PostingDate });
            builder.HasIndex(e => new { e.CompanyId, e.TransactionNo });

            builder.HasOne<TParty>()
                   .WithMany()
                   .HasForeignKey(e => e.PartyId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Ignore(e => e.Kind);
            builder.Ignore(e => e.AppliedAmount);
        });

    private void ConfigureApplication<TApplication, TEntry>(ModelBuilder modelBuilder, string table)
        where TApplication : PartyApplication
        where TEntry : PartyLedgerEntry
        => modelBuilder.Entity<TApplication>(builder =>
        {
            builder.ToTable(table, SchemaName);

            builder.Property(a => a.Amount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(a => a.AmountInCurrency).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(a => a.FromBaseAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(a => a.ToBaseAmount).HasColumnType(DecimalPrecisionConventions.Money);
            builder.Property(a => a.ExchangeDifference).HasColumnType(DecimalPrecisionConventions.Money);

            // Read from both ends: what settled this invoice, and what this payment went towards.
            builder.HasIndex(a => new { a.CompanyId, a.AppliedToEntryId });
            builder.HasIndex(a => new { a.CompanyId, a.AppliedFromEntryId });

            builder.HasOne<TEntry>()
                   .WithMany()
                   .HasForeignKey(a => a.AppliedFromEntryId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<TEntry>()
                   .WithMany()
                   .HasForeignKey(a => a.AppliedToEntryId)
                   .OnDelete(DeleteBehavior.Restrict);

            builder.Ignore(a => a.Kind);
        });

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
    }
}
