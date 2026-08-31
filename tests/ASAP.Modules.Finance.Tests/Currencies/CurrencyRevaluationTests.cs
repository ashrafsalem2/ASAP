using ASAP.Modules.Finance.Currencies;
using ASAP.Modules.Finance.Parties;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Currencies;

/// <summary>
/// What an open foreign balance is worth at a closing rate, and which way the difference goes.
/// </summary>
/// <remarks>
/// The sign is the thing most easily got backwards, and getting it backwards turns every gain
/// into a loss in the income statement without anything failing.
/// </remarks>
public sealed class CurrencyRevaluationArithmeticTests
{
    /// <summary>A receivable worth more than it is carried at is a gain.</summary>
    [Fact]
    public void A_receivable_worth_more_is_a_gain()
    {
        // A thousand dollars raised at 3.75 and now worth 3.80.
        var difference = CurrencyRevaluation.Difference(1000m, 3750m, 3.80m);

        difference.ShouldBe(-50m, "negative is a gain, the same way the settlement poster means it");
    }

    /// <summary>And one worth less is a loss.</summary>
    [Fact]
    public void A_receivable_worth_less_is_a_loss()
        => CurrencyRevaluation.Difference(1000m, 3800m, 3.75m).ShouldBe(50m);

    /// <summary>
    /// A balance already carried at the closing rate posts nothing.
    /// </summary>
    /// <remarks>
    /// The property that makes the run safe to repeat: it measures against what the balance is
    /// carried at, so a second run on the same date has nothing left to say.
    /// </remarks>
    [Fact]
    public void A_balance_already_at_the_closing_rate_posts_nothing()
        => CurrencyRevaluation.Difference(1000m, 3800m, 3.80m).ShouldBe(0m);

    /// <summary>Running twice is running once.</summary>
    [Fact]
    public void Running_twice_is_running_once()
    {
        var carrying = 3750m;

        var first = CurrencyRevaluation.Difference(1000m, carrying, 3.80m);
        carrying = CurrencyRevaluation.Revalued(1000m, 3.80m);

        var second = CurrencyRevaluation.Difference(1000m, carrying, 3.80m);

        first.ShouldBe(-50m);
        second.ShouldBe(0m, "no reversal is needed because nothing was left to reverse");
    }

    /// <summary>A payable, which carries the opposite sign, works the same way.</summary>
    [Fact]
    public void A_payable_owing_more_is_a_loss()
    {
        // Owing a thousand dollars is a negative balance. At a higher rate the company owes more
        // in riyals, which is a loss.
        var difference = CurrencyRevaluation.Difference(-1000m, -3750m, 3.80m);

        difference.ShouldBe(50m);
    }

    /// <summary>A part-settled balance is revalued on what is left, not on what it was.</summary>
    [Fact]
    public void Only_what_is_left_is_revalued()
        => CurrencyRevaluation.Difference(400m, 1500m, 3.80m).ShouldBe(-20m);

    /// <summary>The revalued figure is rounded to the money it will be carried at.</summary>
    [Fact]
    public void The_revalued_figure_is_rounded_to_money()
        => CurrencyRevaluation.Revalued(333m, 3.7533m).ShouldBe(1249.85m);
}

/// <summary>
/// Which balances a run picks up, and what it says about them.
/// </summary>
/// <remarks>
/// The preview is what somebody looks at before closing a month, so it has to be right about
/// which entries are in scope as much as about the arithmetic.
/// </remarks>
public sealed class CurrencyRevaluationTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000f7");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000f7");
    private static readonly DateOnly Closing = new(2026, 6, 30);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new() { TenantId = Tenant, CompanyId = Company };
    private readonly StubClock _clock = new(new DateTime(2026, 7, 5, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    /// <summary>Sets up a dollar at 3.75 in January and 3.80 from June.</summary>
    public CurrencyRevaluationTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-fx-reval-{Guid.CreateVersion7()}")
            .Options;

        using var context = NewContext();

        context.Set<Currency>().Add(new Currency
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "USD",
            Name = "US dollar",
            Rates =
            [
                Rate(new DateOnly(2026, 1, 1), 3.75m),
                Rate(new DateOnly(2026, 6, 1), 3.80m),
            ],
        });

        // No rate at all, so a balance in it cannot be closed.
        context.Set<Currency>().Add(new Currency
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "EUR",
            Name = "Euro",
        });

        context.SaveChanges();

        static ExchangeRate Rate(DateOnly from, decimal baseAmount) => new()
        {
            TenantId = Tenant,
            CompanyId = Company,
            StartingDate = from,
            CurrencyAmount = 1m,
            BaseAmount = baseAmount,
        };
    }

    /// <summary>An open dollar invoice raised at the old rate is picked up and restated.</summary>
    [Fact]
    public async Task An_open_foreign_invoice_is_restated()
    {
        using var context = NewContext();

        Add(context, "C-0001", "INV-1", 3750m, 1000m, "USD");

        var run = await Service(context).PreviewAsync(Closing, PartyKind.Customer);

        run.Succeeded.ShouldBeTrue();
        run.Value.Rows.Count.ShouldBe(1);

        var row = run.Value.Rows[0];

        row.RemainingInCurrency.ShouldBe(1000m);
        row.CarryingAmount.ShouldBe(3750m);
        row.RevaluedAmount.ShouldBe(3800m);
        row.Difference.ShouldBe(-50m, "the company is fifty better off");
        run.Value.TotalDifference.ShouldBe(-50m);
    }

    /// <summary>A balance in the company's own currency is not touched.</summary>
    [Fact]
    public async Task A_balance_in_the_companys_own_currency_is_left_alone()
    {
        using var context = NewContext();

        Add(context, "C-0001", "INV-1", 5000m, null, null);

        (await Service(context).PreviewAsync(Closing, PartyKind.Customer))
            .Value.Rows.ShouldBeEmpty();
    }

    /// <summary>A settled invoice is not open, so nothing is said about it.</summary>
    [Fact]
    public async Task A_settled_invoice_is_not_revalued()
    {
        using var context = NewContext();

        Add(context, "C-0001", "INV-1", 3750m, 1000m, "USD", isOpen: false, remainingInCurrency: 0m);

        (await Service(context).PreviewAsync(Closing, PartyKind.Customer))
            .Value.Rows.ShouldBeEmpty();
    }

    /// <summary>An invoice raised after the closing date is not in the period being closed.</summary>
    [Fact]
    public async Task An_invoice_raised_after_the_date_is_out_of_scope()
    {
        using var context = NewContext();

        Add(context, "C-0001", "INV-1", 3750m, 1000m, "USD", postingDate: new DateOnly(2026, 7, 1));

        (await Service(context).PreviewAsync(Closing, PartyKind.Customer))
            .Value.Rows.ShouldBeEmpty();
    }

    /// <summary>A balance already carried at the closing rate is left out of the run entirely.</summary>
    [Fact]
    public async Task A_balance_already_at_the_rate_is_left_out()
    {
        using var context = NewContext();

        Add(context, "C-0001", "INV-1", 3800m, 1000m, "USD");

        var run = await Service(context).PreviewAsync(Closing, PartyKind.Customer);

        run.Value.Rows.ShouldBeEmpty("reporting it would bury the ones that did move");
    }

    /// <summary>
    /// A currency with no closing rate refuses the run, once, rather than per invoice.
    /// </summary>
    /// <remarks>
    /// Forty invoices in a currency with no rate is one thing wrong, not forty.
    /// </remarks>
    [Fact]
    public async Task A_currency_with_no_rate_refuses_the_run_once()
    {
        using var context = NewContext();

        Add(context, "C-0001", "INV-1", 4000m, 1000m, "EUR");
        Add(context, "C-0002", "INV-2", 4000m, 1000m, "EUR");

        var run = await Service(context).PreviewAsync(Closing, PartyKind.Customer);

        run.Failed.ShouldBeTrue();
        run.Messages.Count(static m => m.IsFailure).ShouldBe(1);
    }

    /// <summary>Vendors are revalued in their own run, and their sign is the other way.</summary>
    [Fact]
    public async Task A_vendor_balance_is_revalued_the_other_way()
    {
        using var context = NewContext();

        context.Set<VendorLedgerEntry>().Add(new VendorLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PartyNo = "V-0001",
            PartyName = "A vendor",
            Description = "Bill",
            PostingDate = new DateOnly(2026, 3, 1),
            DueDate = new DateOnly(2026, 4, 1),
            Amount = -3750m,
            RemainingAmount = -3750m,
            AmountInCurrency = -1000m,
            RemainingAmountInCurrency = -1000m,
            CurrencyCode = "USD",
            ControlAccountNo = "2100",
            SourceCode = "PURCH",
            IsOpen = true,
        });

        context.SaveChanges();

        var run = await Service(context).PreviewAsync(Closing, PartyKind.Vendor);

        run.Value.Rows.Count.ShouldBe(1);
        run.Value.Rows[0].Difference.ShouldBe(50m, "owing more riyals for the same dollars is a loss");
    }

    /// <summary>Closes every context this test opened.</summary>
    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private static void Add(
        AsapDbContext context,
        string partyNo,
        string documentNo,
        decimal amount,
        decimal? amountInCurrency,
        string? currencyCode,
        bool isOpen = true,
        decimal? remainingInCurrency = null,
        DateOnly? postingDate = null)
    {
        context.Set<CustomerLedgerEntry>().Add(new CustomerLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PartyNo = partyNo,
            PartyName = partyNo,
            Description = documentNo,
            DocumentNo = documentNo,
            PostingDate = postingDate ?? new DateOnly(2026, 3, 1),
            DueDate = (postingDate ?? new DateOnly(2026, 3, 1)).AddDays(30),
            Amount = amount,
            RemainingAmount = isOpen ? amount : 0m,
            AmountInCurrency = amountInCurrency,
            RemainingAmountInCurrency = remainingInCurrency ?? amountInCurrency,
            CurrencyCode = currencyCode,
            ControlAccountNo = "1200",
            SourceCode = "SALES",
            IsOpen = isOpen,
        });

        context.SaveChanges();
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new FinanceSchema()]);

        _opened.Add(context);

        return context;
    }

    /// <summary>
    /// Only the preview is exercised here, so the posting side is not built.
    /// </summary>
    /// <remarks>
    /// Posting needs the whole document poster — accounts, periods, dimensions — and standing that
    /// up in a unit test proves the harness rather than the rule. The posting is driven end to end
    /// against the real database instead, where a wrong account or a closed period would actually
    /// refuse.
    /// </remarks>
    private CurrencyRevaluationService Service(AsapDbContext context)
        => new(
            context,
            new ExchangeRateService(context, new MessageCatalog(FinanceMessages.All)),
            documents: null!,
            setup: null!,
            new MessageCatalog(FinanceMessages.All),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CurrencyRevaluationService>.Instance);

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

        public string? UserName => "accountant";

        public string? DisplayName => "Accountant";

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
}
