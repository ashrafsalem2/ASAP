using ASAP.Modules.Finance.Banking;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Payments;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Payments;

/// <summary>
/// Proposing vendor payments and making the bank file, and the two ways it must not go wrong.
/// </summary>
/// <remarks>
/// Paying an invoice twice, and paying it into an account somebody changed the day before. Posting
/// needs the whole ledger stack and is driven against the real database instead.
/// </remarks>
public sealed class PaymentRunTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000e9");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000e9");
    private static readonly DateOnly Today = new(2026, 9, 13);

    private const string GoodIban = "GB82WEST12345698765432";
    private const string OtherGoodIban = "DE89370400440532013000";

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 9, 13, 9, 0, 0, DateTimeKind.Utc));
    private readonly StubNumbers _numbers = new();
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a company, a bank account, two vendors and their invoices.</summary>
    public PaymentRunTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-payment-runs-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        using var context = NewContext();

        // No company row: its id is generated, so the base currency falls back to riyals here.
        // The company name on the file is exercised against the real database instead.
        context.Set<BankAccount>().Add(new BankAccount
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "SNB-MAIN",
            Name = "Main account",
            Iban = "SA0380000000608010167519",
            GlAccountNo = "1100",
        });

        var gulf = Vendor("V-0001", "Gulf Office Supplies", GoodIban);
        var najd = Vendor("V-0002", "Najd Hardware", null);

        context.Set<Vendor>().AddRange(gulf, najd, Vendor("V-0003", "Blocked Ltd", OtherGoodIban, blocked: true));
        context.SaveChanges();

        Invoice(context, gulf, "INV-1001", 1000m, Today.AddDays(-5));
        Invoice(context, gulf, "INV-1002", 250.50m, Today.AddDays(1));
        Invoice(context, gulf, "INV-1003", 900m, Today.AddDays(30));
        Invoice(context, najd, "N-77", 400m, Today.AddDays(-1));

        context.SaveChanges();

        static Vendor Vendor(string no, string name, string? iban, bool blocked = false) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = no,
            Name = name,
            Iban = iban,
            IsBlocked = blocked,
        };
    }

    /// <summary>Everything due by the date is proposed, one transfer per vendor.</summary>
    [Fact]
    public async Task What_is_due_is_proposed_one_transfer_per_vendor()
    {
        using var context = NewContext();

        var run = await Runs(context).ProposeAsync("SNB-MAIN", Today.AddDays(2), Today.AddDays(2));

        run.Succeeded.ShouldBeTrue();
        run.Value.Status.ShouldBe(PaymentRunStatus.Draft);
        run.Value.CurrencyCode.ShouldBe("SAR");
        run.Value.Lines.Count.ShouldBe(2);

        var gulf = run.Value.Lines.Single(l => l.VendorNo == "V-0001");

        gulf.Amount.ShouldBe(1250.50m, "INV-1003 is not due for a month");
        gulf.Invoices.Count.ShouldBe(2);
        gulf.Iban.ShouldBe(GoodIban);
        gulf.Reference.ShouldBe("INV-1001, INV-1002");
    }

    /// <summary>
    /// An invoice already on a live run is not proposed again.
    /// </summary>
    /// <remarks>
    /// The failure this whole design exists to prevent. Two runs made the same morning must not
    /// both carry one invoice.
    /// </remarks>
    [Fact]
    public async Task An_invoice_on_a_live_run_is_not_proposed_again()
    {
        using var context = NewContext();
        var runs = Runs(context);

        (await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2))).Succeeded.ShouldBeTrue();

        var second = await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));

        second.Failed.ShouldBeTrue();
        second.Messages.ShouldContain(m => m.Code == FinanceMessages.NothingDueToPay);
    }

    /// <summary>A cancelled run gives its invoices back.</summary>
    [Fact]
    public async Task A_cancelled_run_gives_its_invoices_back()
    {
        using var context = NewContext();
        var runs = Runs(context);

        var first = await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));
        await runs.CancelAsync(first.Value.No);

        (await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2))).Succeeded.ShouldBeTrue();
    }

    /// <summary>A blocked vendor is left out, and the run says so.</summary>
    [Fact]
    public async Task A_blocked_vendor_is_left_out_and_said()
    {
        using var context = NewContext();

        var blocked = await context.Set<Vendor>().FirstAsync(v => v.No == "V-0003");
        Invoice(context, blocked, "B-1", 99m, Today);
        await context.SaveChangesAsync();

        var run = await Runs(context).ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));

        run.Value.Lines.ShouldNotContain(l => l.VendorNo == "V-0003");
        run.Messages.ShouldContain(m => m.Code == FinanceMessages.PaymentBlockedVendorLeftOut);
    }

    /// <summary>A vendor with no valid IBAN stops the export, not the proposal.</summary>
    [Fact]
    public async Task A_vendor_without_an_iban_stops_the_export()
    {
        using var context = NewContext();
        var runs = Runs(context);

        var run = await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));

        var exported = await runs.ExportAsync(run.Value.No);

        exported.Failed.ShouldBeTrue();
        exported.Messages.ShouldContain(m => m.Code == FinanceMessages.VendorIbanInvalid);
    }

    /// <summary>Taking the bad line off lets the rest go.</summary>
    [Fact]
    public async Task Taking_the_line_off_lets_the_rest_go()
    {
        using var context = NewContext();
        var runs = Runs(context);

        var run = await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));
        var najd = run.Value.Lines.Single(l => l.VendorNo == "V-0002");

        await runs.RemoveLineAsync(run.Value.No, najd.LineNo);

        var exported = await runs.ExportAsync(run.Value.No);

        exported.Succeeded.ShouldBeTrue();
        exported.Value.TransferCount.ShouldBe(1);
        exported.Value.ControlSum.ShouldBe(1250.50m);

        var saved = await runs.LoadAsync(run.Value.No);

        saved!.Status.ShouldBe(PaymentRunStatus.Exported);
        saved.FileSha256.ShouldBe(exported.Value.Sha256);
        saved.FileContent.ShouldNotBeNull().ShouldContain(GoodIban);
    }

    /// <summary>
    /// Bank details changed after proposal stop the export.
    /// </summary>
    /// <remarks>
    /// New bank details announced just before a payment run is the commonest shape payment fraud
    /// takes. The run pays where it was proposed to pay, or not at all.
    /// </remarks>
    [Fact]
    public async Task Bank_details_changed_after_proposal_stop_the_export()
    {
        using var context = NewContext();
        var runs = Runs(context);

        var run = await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));
        await runs.RemoveLineAsync(run.Value.No, run.Value.Lines.Single(l => l.VendorNo == "V-0002").LineNo);

        var gulf = await context.Set<Vendor>().FirstAsync(v => v.No == "V-0001");
        gulf.Iban = OtherGoodIban;
        await context.SaveChangesAsync();

        var exported = await runs.ExportAsync(run.Value.No);

        exported.Failed.ShouldBeTrue();
        exported.Messages.ShouldContain(m => m.Code == FinanceMessages.VendorIbanChangedSinceProposal);
    }

    /// <summary>An exported run cannot have lines taken off.</summary>
    [Fact]
    public async Task An_exported_run_cannot_be_changed()
    {
        using var context = NewContext();
        var runs = Runs(context);

        var run = await runs.ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));
        await runs.RemoveLineAsync(run.Value.No, run.Value.Lines.Single(l => l.VendorNo == "V-0002").LineNo);
        await runs.ExportAsync(run.Value.No);

        var removed = await runs.RemoveLineAsync(run.Value.No, 10);

        removed.Failed.ShouldBeTrue("a line taken off now is a payment the bank still makes");
        removed.Messages.ShouldContain(m => m.Code == FinanceMessages.PaymentRunWrongStage);

        (await runs.ExportAsync(run.Value.No)).Failed.ShouldBeTrue("the file is made once");
    }

    /// <summary>A bank account with no IBAN cannot pay anybody.</summary>
    [Fact]
    public async Task A_bank_account_without_an_iban_cannot_pay()
    {
        using var context = NewContext();

        var bank = await context.Set<BankAccount>().FirstAsync();
        bank.Iban = "SA00 1234";
        await context.SaveChangesAsync();

        var run = await Runs(context).ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));

        run.Failed.ShouldBeTrue();
        run.Messages.ShouldContain(m => m.Code == FinanceMessages.PaymentBankAccountHasNoIban);
    }

    /// <summary>Invoices in another currency are left out of a riyal run, and it says how many.</summary>
    [Fact]
    public async Task Other_currencies_are_left_out_and_counted()
    {
        using var context = NewContext();

        var gulf = await context.Set<Vendor>().FirstAsync(v => v.No == "V-0001");
        var dollar = Invoice(context, gulf, "USD-1", 3750m, Today);
        dollar.CurrencyCode = "USD";
        dollar.AmountInCurrency = -1000m;
        dollar.RemainingAmountInCurrency = -1000m;
        await context.SaveChangesAsync();

        var run = await Runs(context).ProposeAsync("SNB-MAIN", Today, Today.AddDays(2));

        run.Value.Lines.SelectMany(l => l.Invoices).ShouldNotContain(i => i.DocumentNo == "USD-1");
        run.Messages.ShouldContain(m => m.Code == FinanceMessages.PaymentOtherCurrencyLeftOut);
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static VendorLedgerEntry Invoice(AsapDbContext context, Vendor vendor, string documentNo, decimal amount, DateOnly due)
    {
        var entry = new VendorLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PartyId = vendor.Id,
            PartyNo = vendor.No,
            PartyName = vendor.Name,
            Description = documentNo,
            DocumentNo = documentNo,
            PostingDate = due.AddDays(-30),
            DueDate = due,
            Amount = -amount,
            RemainingAmount = -amount,
            ControlAccountNo = "2100",
            SourceCode = "PURCH",
            IsOpen = true,
        };

        context.Set<VendorLedgerEntry>().Add(entry);

        return entry;
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new FinanceSchema()]);

        _opened.Add(context);

        return context;
    }

    private PaymentRunService Runs(AsapDbContext context)
        => new(
            context,
            documents: null!,
            applications: null!,
            new MessageCatalog([.. PlatformMessages.All, .. FinanceMessages.All]),
            _numbers,
            new StubSetup(),
            _tenancy,
            new StubUser(),
            _clock,
            NullLogger<PaymentRunService>.Instance);

    private sealed class StubNumbers : INumberSeriesService
    {
        private int _next;

        public Task<Result<string>> NextAsync(string s, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result<string>.Success($"PR-2026-{++_next:0000}"));

        public Task<Result<string>> PeekAsync(string s, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result<string>.Success($"PR-2026-{_next + 1:0000}"));

        public Task<Result> ValidateManualAsync(string s, string n, DateOnly d, CancellationToken c = default)
            => Task.FromResult(Result.Success());
    }

    private sealed class StubTenant : ITenantContext
    {
        public Guid? TenantId { get; set; }

        public Guid? CompanyId { get; set; }

        public Guid? BranchId { get; set; }

        public bool IsCrossTenantOperation { get; set; }

        public Guid RequireTenantId() => TenantId ?? Guid.Empty;

        public Guid RequireCompanyId() => CompanyId ?? Guid.Empty;
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? UserId => Guid.Empty;

        public string? UserName => "payables";

        public string? DisplayName => "Payables";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => Guid.Empty;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class StubSetup : ISetupService
    {
        public IReadOnlyCollection<SetupDescriptor> Declared => [];

        public SetupDescriptor? Describe(string key) => null;

        public ValueTask<TValue> GetAsync<TValue>(string key, CancellationToken cancellationToken = default)
            => ValueTask.FromResult((TValue)(object)"PAYRUN");

        public ValueTask<TValue?> GetAtScopeAsync<TValue>(
            string key,
            SetupScope scope,
            Guid? scopeId = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<TValue?>(default);

        public Task<Result> SetAsync(
            string key,
            string? value,
            SetupScope scope = SetupScope.Company,
            Guid? scopeId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Result.Success());
    }
}
