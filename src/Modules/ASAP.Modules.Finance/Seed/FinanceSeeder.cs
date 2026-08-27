using System.Globalization;
using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Periods;
using ASAP.Modules.Finance.Tax;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Finance.Seed;

/// <summary>
/// Gives a new company a working chart of accounts, a fiscal calendar and a journal batch.
/// </summary>
/// <remarks>
/// <para>
/// A starting chart rather than an empty one, because an empty chart of accounts is not a blank
/// canvas: it is a week of work before anyone can post anything, and every business needs the same
/// two dozen accounts to begin with. Accounts are numbered on the usual convention -- 1 assets,
/// 2 liabilities, 3 equity, 4 income, 5 cost of sales, 6 expenses -- with gaps left for a company
/// to insert its own.
/// </para>
/// <para>
/// Runs only for a company that has no accounts yet, and never modifies what it finds. A seeder
/// that reasserts its idea of the right chart will one day overwrite a customer's.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="logger">Reports what was created.</param>
public sealed class FinanceSeeder(AsapDbContext context, ILogger<FinanceSeeder> logger)
{
    /// <summary>
    /// Seeds Finance for one company, if it has nothing yet.
    /// </summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="companyId">The company to set up.</param>
    /// <param name="year">The financial year to open, normally the current one.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>True when it seeded, false when the company already had accounts.</returns>
    public async Task<bool> SeedAsync(
        Guid tenantId,
        Guid companyId,
        int year,
        CancellationToken cancellationToken = default)
    {
        var hasAccounts = await ExistsAsync<GlAccount>(companyId, cancellationToken).ConfigureAwait(false);
        var hasParties = await ExistsAsync<Parties.Customer>(companyId, cancellationToken).ConfigureAwait(false);
        var hasTaxCodes = await ExistsAsync<Tax.TaxCode>(companyId, cancellationToken).ConfigureAwait(false);

        if (hasAccounts && hasParties && hasTaxCodes)
        {
            return false;
        }

        // Each unit guards on its own data rather than on the chart of accounts. Gating everything
        // behind one check means a company set up before a later unit existed never receives it --
        // which is exactly what happened when customers and vendors arrived: the tables were
        // created, the screens shipped, and every existing company saw them empty.
        if (!hasAccounts)
        {
            SeedChartOfAccounts(tenantId, companyId);
            SeedFiscalYear(tenantId, companyId, year);
            SeedJournalBatches(tenantId, companyId);
        }

        if (!hasParties)
        {
            SeedParties(tenantId, companyId);
        }

        if (!hasTaxCodes)
        {
            SeedTaxCodes(tenantId, companyId);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded Finance for company {Company}: chart and calendar {Chart}, "
            + "customers and vendors {Parties}, tax codes {Tax}.",
            companyId,
            hasAccounts ? "already present" : $"created for {year}",
            hasParties ? "already present" : "created",
            hasTaxCodes ? "already present" : "created");

        return true;
    }

    private Task<bool> ExistsAsync<TEntity>(Guid companyId, CancellationToken cancellationToken)
        where TEntity : ASAP.Platform.Kernel.Entities.CompanyEntity
        => context.Set<TEntity>()
            .IgnoreQueryFilters()
            .AnyAsync(e => e.CompanyId == companyId, cancellationToken);

    private void SeedChartOfAccounts(Guid tenantId, Guid companyId)
    {
        var accounts = new List<GlAccount>();

        void Add(
            string no,
            string name,
            string arabic,
            GlAccountCategory category,
            GlAccountType type = GlAccountType.Posting,
            int indent = 1,
            string? totaling = null,
            bool directPosting = true)
            => accounts.Add(new GlAccount
            {
                TenantId = tenantId,
                CompanyId = companyId,
                No = no,
                Name = name,
                NameArabic = arabic,
                Category = category,
                AccountType = type,
                Indentation = indent,
                Totaling = totaling,
                AllowsDirectPosting = directPosting,
            });

        // Assets
        Add("1000", "ASSETS", "الأصول", GlAccountCategory.Assets, GlAccountType.Heading, 0);
        Add("1100", "Cash on hand", "النقد بالصندوق", GlAccountCategory.Assets);
        Add("1110", "Bank current account", "الحساب الجاري بالبنك", GlAccountCategory.Assets);

        // Control accounts. Direct posting is off: these are written by the module that owns the
        // subsidiary ledger, and a hand-keyed entry makes the control disagree with the ledger
        // behind it -- a difference nobody finds until year end.
        Add("1300", "Accounts receivable", "الذمم المدينة", GlAccountCategory.Assets, directPosting: false);
        Add("1400", "Inventory", "المخزون", GlAccountCategory.Assets, directPosting: false);
        Add("1500", "VAT recoverable", "ضريبة القيمة المضافة القابلة للاسترداد", GlAccountCategory.Assets, directPosting: false);
        Add("1600", "Prepaid expenses", "مصروفات مدفوعة مقدمًا", GlAccountCategory.Assets);
        Add("1700", "Fixed assets", "الأصول الثابتة", GlAccountCategory.Assets);
        Add("1790", "Accumulated depreciation", "مجمع الإهلاك", GlAccountCategory.Assets);
        Add("1999", "TOTAL ASSETS", "إجمالي الأصول", GlAccountCategory.Assets, GlAccountType.Total, 0, "1000..1998");

        // Liabilities
        Add("2000", "LIABILITIES", "الخصوم", GlAccountCategory.Liabilities, GlAccountType.Heading, 0);
        Add("2100", "Accounts payable", "الذمم الدائنة", GlAccountCategory.Liabilities, directPosting: false);
        Add("2200", "VAT payable", "ضريبة القيمة المضافة المستحقة", GlAccountCategory.Liabilities, directPosting: false);
        Add("2300", "Accrued expenses", "مصروفات مستحقة", GlAccountCategory.Liabilities);
        Add("2400", "Payroll payable", "رواتب مستحقة", GlAccountCategory.Liabilities, directPosting: false);
        Add("2500", "End of service provision", "مخصص نهاية الخدمة", GlAccountCategory.Liabilities);
        Add("2999", "TOTAL LIABILITIES", "إجمالي الخصوم", GlAccountCategory.Liabilities, GlAccountType.Total, 0, "2000..2998");

        // Equity
        Add("3000", "EQUITY", "حقوق الملكية", GlAccountCategory.Equity, GlAccountType.Heading, 0);
        Add("3100", "Share capital", "رأس المال", GlAccountCategory.Equity);
        Add("3200", "Retained earnings", "الأرباح المبقاة", GlAccountCategory.Equity);
        Add("3300", "Current year result", "نتيجة السنة الحالية", GlAccountCategory.Equity, directPosting: false);

        // Income
        Add("4000", "INCOME", "الإيرادات", GlAccountCategory.Income, GlAccountType.Heading, 0);
        Add("4100", "Sales revenue", "إيرادات المبيعات", GlAccountCategory.Income, directPosting: false);
        Add("4200", "Sales returns", "مردودات المبيعات", GlAccountCategory.Income, directPosting: false);

        // Discount given lands on its own account rather than netting against revenue, so the cost
        // of promotion is visible in the profit and loss instead of hidden inside a lower sales figure.
        Add("4300", "Discounts given", "الخصومات الممنوحة", GlAccountCategory.Income, directPosting: false);
        Add("4900", "Other income", "إيرادات أخرى", GlAccountCategory.Income);

        // Cost of sales
        Add("5000", "COST OF SALES", "تكلفة المبيعات", GlAccountCategory.CostOfGoodsSold, GlAccountType.Heading, 0);
        Add("5100", "Cost of goods sold", "تكلفة البضاعة المباعة", GlAccountCategory.CostOfGoodsSold, directPosting: false);
        Add("5200", "Inventory adjustment", "تسوية المخزون", GlAccountCategory.CostOfGoodsSold, directPosting: false);
        Add("5300", "Purchase variance", "فروقات الشراء", GlAccountCategory.CostOfGoodsSold, directPosting: false);

        // Expenses
        Add("6000", "EXPENSES", "المصروفات", GlAccountCategory.Expense, GlAccountType.Heading, 0);
        Add("6100", "Salaries and wages", "الرواتب والأجور", GlAccountCategory.Expense);
        Add("6200", "Rent", "الإيجار", GlAccountCategory.Expense);
        Add("6300", "Utilities", "المرافق", GlAccountCategory.Expense);
        Add("6400", "Office expenses", "مصروفات مكتبية", GlAccountCategory.Expense);
        Add("6500", "Marketing", "التسويق", GlAccountCategory.Expense);
        Add("6600", "Depreciation", "الإهلاك", GlAccountCategory.Expense);
        Add("6900", "Other expenses", "مصروفات أخرى", GlAccountCategory.Expense);
        Add("6999", "TOTAL EXPENSES", "إجمالي المصروفات", GlAccountCategory.Expense, GlAccountType.Total, 0, "6000..6998");

        context.Set<GlAccount>().AddRange(accounts);
    }

    /// <summary>
    /// Opens a financial year with twelve calendar months.
    /// </summary>
    /// <remarks>
    /// Twelve months because that is what almost every company wants and none of them want to key
    /// by hand. A company on a different structure edits the periods afterwards; a company on a
    /// non-calendar year edits the dates. Both are far less work than building a calendar from nothing.
    /// </remarks>
    private void SeedFiscalYear(Guid tenantId, Guid companyId, int year)
    {
        var fiscalYear = new FiscalYear
        {
            TenantId = tenantId,
            CompanyId = companyId,
            Code = year.ToString(CultureInfo.InvariantCulture),
            StartDate = new DateOnly(year, 1, 1),
            EndDate = new DateOnly(year, 12, 31),
        };

        for (var month = 1; month <= 12; month++)
        {
            var start = new DateOnly(year, month, 1);

            fiscalYear.Periods.Add(new FiscalPeriod
            {
                TenantId = tenantId,
                CompanyId = companyId,
                FiscalYearId = fiscalYear.Id,
                PeriodNo = month,
                Name = start.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-GB")),
                NameArabic = start.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("ar-SA")),
                StartDate = start,
                EndDate = start.AddMonths(1).AddDays(-1),
            });
        }

        context.Set<FiscalYear>().Add(fiscalYear);
    }

    private void SeedJournalBatches(Guid tenantId, Guid companyId)
    {
        context.Set<GeneralJournalBatch>().AddRange(
            new GeneralJournalBatch
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Code = "DEFAULT",
                Description = "General journal",
                NumberSeriesCode = "GJ",
                SourceCode = "GENJNL",
            },
            new GeneralJournalBatch
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Code = "MONTHEND",
                Description = "Month-end accruals and adjustments",
                NumberSeriesCode = "GJ",
                SourceCode = "GENJNL",
            });
    }

    /// <summary>
    /// A handful of customers and vendors to trade with.
    /// </summary>
    /// <remarks>
    /// Deliberately varied rather than uniform: different payment terms, one customer with a tight
    /// credit limit and one with none, and one party that is both. Seed data where every row is
    /// the same teaches nothing and hides every rule that only fires on the unusual case.
    /// </remarks>
    private void SeedParties(Guid tenantId, Guid companyId)
    {
        void Customer(string no, string name, string arabic, int terms, decimal limit)
            => context.Set<Parties.Customer>().Add(new Parties.Customer
            {
                TenantId = tenantId,
                CompanyId = companyId,
                No = no,
                Name = name,
                NameArabic = arabic,
                PaymentTermsDays = terms,
                CreditLimit = limit,
            });

        void Vendor(string no, string name, string arabic, int terms)
            => context.Set<Parties.Vendor>().Add(new Parties.Vendor
            {
                TenantId = tenantId,
                CompanyId = companyId,
                No = no,
                Name = name,
                NameArabic = arabic,
                PaymentTermsDays = terms,
            });

        Customer("C-0001", "Al Faisaliah Trading", "الفيصلية للتجارة", 30, 50_000m);
        Customer("C-0002", "Najd Contracting", "نجد للمقاولات", 60, 250_000m);
        Customer("C-0003", "Rawabi Retail", "روابي للتجزئة", 14, 15_000m);

        // No limit at all, which is the case that catches a credit check written as
        // "balance > limit" without asking whether a limit was set.
        Customer("C-0004", "Cash sales", "مبيعات نقدية", 0, 0m);

        Vendor("V-0001", "Gulf Office Supplies", "الخليج للأدوات المكتبية", 30);
        Vendor("V-0002", "Riyadh Logistics", "الرياض للخدمات اللوجستية", 45);
        Vendor("V-0003", "Najd Contracting", "نجد للمقاولات", 30);
    }

    /// <summary>
    /// The tax codes a Saudi company actually needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Standard-rated carries both of its historical rates rather than only the current one. The
    /// Kingdom went from 5% to 15% on 1 July 2020, and a system that only knows today's rate
    /// restates every older document the moment anyone touches it -- including a credit note,
    /// which then fails to offset the invoice it corrects.
    /// </para>
    /// <para>
    /// Zero-rated and exempt are seeded separately even though both charge nothing, because they
    /// are not the same thing and belong in different boxes on the return.
    /// </para>
    /// </remarks>
    private void SeedTaxCodes(Guid tenantId, Guid companyId)
    {
        TaxCode Add(
            string code,
            string description,
            string arabic,
            TaxKind kind,
            params (DateOnly From, decimal Percentage)[] rates)
        {
            var taxCode = new TaxCode
            {
                TenantId = tenantId,
                CompanyId = companyId,
                Code = code,
                Description = description,
                DescriptionArabic = arabic,
                Kind = kind,
                OutputAccountNo = "2200",
                InputAccountNo = "1500",
            };

            foreach (var (from, percentage) in rates)
            {
                taxCode.Rates.Add(new TaxRate
                {
                    TenantId = tenantId,
                    CompanyId = companyId,
                    StartingDate = from,
                    Percentage = percentage,
                });
            }

            context.Set<TaxCode>().Add(taxCode);
            return taxCode;
        }

        Add(
            "VAT",
            "Value added tax, standard rate",
            "ضريبة القيمة المضافة، النسبة الأساسية",
            TaxKind.Standard,
            (new DateOnly(2018, 1, 1), 5m),
            (new DateOnly(2020, 7, 1), 15m));

        Add("VAT-Z", "Zero rated", "خاضع لنسبة صفر", TaxKind.ZeroRated);
        Add("VAT-E", "Exempt", "معفى", TaxKind.Exempt);

        Add(
            "VAT-RC",
            "Reverse charge, imported services",
            "الاحتساب العكسي، خدمات مستوردة",
            TaxKind.ReverseCharge,
            (new DateOnly(2020, 7, 1), 15m));
    }
}
