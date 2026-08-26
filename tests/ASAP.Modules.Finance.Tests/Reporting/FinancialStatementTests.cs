using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Periods;
using ASAP.Modules.Finance.Reporting;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Reporting;

/// <summary>
/// Covers the two statements everybody reads.
/// </summary>
/// <remarks>
/// The interesting case is the balance sheet before a year-end transfer has run. Profit earned
/// belongs to the owners, but it does not reach an equity account until the transfer, so a
/// statement built only from balance sheet accounts is out by exactly the profit made -- and in a
/// company that has not traded it is out by nothing, which is why the mistake survives testing.
/// </remarks>
public sealed class FinancialStatementTests : IDisposable
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly DateOnly YearStart = new(2026, 1, 1);
    private static readonly DateOnly Today = new(2026, 8, 26);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    public FinancialStatementTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-fin-{Guid.CreateVersion7()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics
                .InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Seed();
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
        }
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new FinanceSchema()]);
        _opened.Add(context);
        return context;
    }

    private void Seed()
    {
        using var context = NewContext();

        void Account(string no, string name, GlAccountCategory category)
            => context.Set<GlAccount>().Add(new GlAccount
            {
                TenantId = Tenant,
                CompanyId = Company,
                No = no,
                Name = name,
                AccountType = GlAccountType.Posting,
                Category = category,
            });

        Account("1100", "Bank", GlAccountCategory.Assets);
        Account("1400", "Inventory", GlAccountCategory.Assets);
        Account("2100", "Trade payables", GlAccountCategory.Liabilities);
        Account("3100", "Share capital", GlAccountCategory.Equity);
        Account("4100", "Sales", GlAccountCategory.Income);
        Account("5100", "Cost of goods sold", GlAccountCategory.CostOfGoodsSold);
        Account("6100", "Rent", GlAccountCategory.Expense);

        context.Set<FiscalYear>().Add(new FiscalYear
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "2026",
            StartDate = YearStart,
            EndDate = new DateOnly(2026, 12, 31),
        });

        context.SaveChanges();
    }

    /// <summary>Posts one balanced pair. Amount is the debit; the contra takes the credit.</summary>
    private void Post(DateOnly date, string debitAccount, string creditAccount, decimal amount)
    {
        using var context = NewContext();

        void Entry(string accountNo, decimal signed)
            => context.Set<GlEntry>().Add(new GlEntry
            {
                TenantId = Tenant,
                CompanyId = Company,
                AccountNo = accountNo,
                PostingDate = date,
                Amount = signed,
                DebitAmount = signed > 0 ? signed : 0m,
                CreditAmount = signed < 0 ? -signed : 0m,
                TransactionNo = 1,
                SourceCode = "TEST",
                Description = "Test entry",
            });

        Entry(debitAccount, amount);
        Entry(creditAccount, -amount);

        context.SaveChanges();
    }

    private void TradeTheYear()
    {
        Post(new DateOnly(2026, 3, 1), "1100", "3100", 10_000m);   // Capital paid in
        Post(new DateOnly(2026, 4, 1), "1400", "2100", 6_000m);    // Stock bought on credit
        Post(new DateOnly(2026, 5, 1), "1100", "4100", 9_000m);    // Sold for cash
        Post(new DateOnly(2026, 5, 1), "5100", "1400", 5_000m);    // Cost of what was sold
        Post(new DateOnly(2026, 6, 1), "6100", "1100", 1_200m);    // Rent paid
    }

    private async Task<IncomeStatement> IncomeAsync(DateOnly from, DateOnly to, bool compare = false)
    {
        await using var context = NewContext();

        return await new IncomeStatementQueryHandler(new LedgerBalances(context))
            .HandleAsync(new IncomeStatementQuery(
                from,
                to,
                compare ? from.AddYears(-1) : null,
                compare ? to.AddYears(-1) : null));
    }

    private async Task<BalanceSheet> BalanceAsync(DateOnly asAt)
    {
        await using var context = NewContext();

        return await new BalanceSheetQueryHandler(new LedgerBalances(context))
            .HandleAsync(new BalanceSheetQuery(asAt));
    }

    [Fact]
    public async Task Revenue_reads_as_a_positive_number()
    {
        TradeTheYear();

        var statement = await IncomeAsync(YearStart, Today);

        var income = statement.Sections.Single(s => s.Category == "Income");

        // Sales are stored as a credit, which is negative. Printing revenue as negative 9,000 is
        // the sort of thing that makes a reader distrust every other figure on the page.
        income.Total.ShouldBe(9_000m);
        income.Rows.ShouldHaveSingleItem().Amount.ShouldBe(9_000m);
    }

    [Fact]
    public async Task Gross_and_net_profit_are_revenue_less_what_it_cost()
    {
        TradeTheYear();

        var statement = await IncomeAsync(YearStart, Today);

        statement.GrossProfit.ShouldBe(4_000m, "9,000 sold less 5,000 it cost");
        statement.NetProfit.ShouldBe(2_800m, "4,000 gross less 1,200 rent");
    }

    [Fact]
    public async Task The_range_is_respected()
    {
        TradeTheYear();

        // Rent was paid in June, so a statement ending in May must not carry it.
        var toMay = await IncomeAsync(YearStart, new DateOnly(2026, 5, 31));

        toMay.NetProfit.ShouldBe(4_000m);
        toMay.Sections.Single(s => s.Category == "Expense").Total.ShouldBe(0m);
    }

    [Fact]
    public async Task A_comparison_range_is_only_present_when_asked_for()
    {
        TradeTheYear();

        (await IncomeAsync(YearStart, Today)).ComparativeNetProfit.ShouldBeNull();

        var compared = await IncomeAsync(YearStart, Today, compare: true);

        compared.ComparativeNetProfit.ShouldBe(0m, "the company did not exist a year earlier");
        compared.ComparativeFrom.ShouldBe(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task The_balance_sheet_balances_while_the_year_is_still_open()
    {
        TradeTheYear();

        var sheet = await BalanceAsync(Today);

        // Bank 10,000 + 9,000 - 1,200 = 17,800. Inventory 6,000 - 5,000 = 1,000.
        sheet.TotalAssets.ShouldBe(18_800m);

        // Payables 6,000 + capital 10,000 + profit 2,800.
        sheet.TotalLiabilitiesAndEquity.ShouldBe(18_800m);

        sheet.IsBalanced.ShouldBeTrue(
            "profit earned belongs to the owners whether or not the year-end transfer has run");

        sheet.ResultForTheYear.ShouldBe(2_800m);
        sheet.UntransferredPriorResult.ShouldBe(0m);
    }

    [Fact]
    public async Task The_result_appears_in_equity_as_a_computed_line()
    {
        TradeTheYear();

        var sheet = await BalanceAsync(Today);
        var equity = sheet.Sections.Single(s => s.Category == "Equity");

        equity.Rows.ShouldContain(r => r.IsComputed && r.Amount == 2_800m);

        // Marked as computed so the screen can say what it is. A reader who sees a figure in
        // equity with no account number behind it deserves to know why.
        equity.Total.ShouldBe(12_800m);
    }

    [Fact]
    public async Task A_company_that_has_not_traded_still_balances()
    {
        // The case that hides the bug: with no profit, a balance sheet that forgets the result is
        // indistinguishable from one that handles it.
        Post(new DateOnly(2026, 3, 1), "1100", "3100", 10_000m);

        var sheet = await BalanceAsync(Today);

        sheet.IsBalanced.ShouldBeTrue();
        sheet.TotalAssets.ShouldBe(10_000m);
        sheet.ResultForTheYear.ShouldBe(0m);
    }

    [Fact]
    public async Task A_loss_reduces_equity_rather_than_increasing_it()
    {
        Post(new DateOnly(2026, 3, 1), "1100", "3100", 10_000m);
        Post(new DateOnly(2026, 6, 1), "6100", "1100", 1_500m);

        var sheet = await BalanceAsync(Today);

        sheet.ResultForTheYear.ShouldBe(-1_500m);
        sheet.TotalAssets.ShouldBe(8_500m);
        sheet.IsBalanced.ShouldBeTrue();
    }

    [Fact]
    public async Task A_result_left_over_from_an_earlier_year_is_reported_separately()
    {
        // Trading before the fiscal year began, with no year-end transfer to move it into equity.
        Post(new DateOnly(2025, 6, 1), "1100", "4100", 4_000m);

        TradeTheYear();

        var sheet = await BalanceAsync(Today);

        // Separated, because this year's result is routine and an untransferred prior year is
        // somebody forgetting to run the close.
        sheet.ResultForTheYear.ShouldBe(2_800m);
        sheet.UntransferredPriorResult.ShouldBe(4_000m);
        sheet.IsBalanced.ShouldBeTrue();
    }

    private sealed class StubTenant : ITenantContext
    {
        public Guid? TenantId => Tenant;

        public Guid? CompanyId => Company;

        public Guid? BranchId => null;

        public bool IsCrossTenantOperation => false;

        public Guid RequireTenantId() => Tenant;

        public Guid RequireCompanyId() => Company;
    }

    private sealed class StubUser : IUserContext
    {
        public Guid? UserId => Guid.Empty;

        public string? UserName => "tests";

        public string? DisplayName => "Tests";

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
