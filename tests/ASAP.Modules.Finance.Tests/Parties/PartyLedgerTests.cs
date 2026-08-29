using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Reporting;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Parties;

/// <summary>
/// Covers the customer ledger: what is outstanding, who settled what, and how late it is.
/// </summary>
/// <remarks>
/// Application is where the quiet errors live. Every operation has to leave both sides consistent,
/// and the ones that matter are the awkward ones -- a part payment, a payment covering two
/// invoices, and undoing a match made by mistake.
/// </remarks>
public sealed class PartyLedgerTests : IDisposable
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 8, 27);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    private Guid _customerId;

    public PartyLedgerTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-parties-{Guid.CreateVersion7()}")
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

    private PartyApplicationService Applications(AsapDbContext context)
        => Applications(context, new FakeExchange());

    private PartyApplicationService Applications(AsapDbContext context, FakeExchange exchange)
        => new(context, new MessageCatalog(FinanceMessages.All), _tenancy, new StubUser(), _clock, exchange);

    private void Seed()
    {
        using var context = NewContext();

        var customer = new Customer
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "C-0001",
            Name = "Al Faisaliah Trading",
            NameArabic = "الفيصلية للتجارة",
            PaymentTermsDays = 30,
        };

        context.Set<Customer>().Add(customer);
        context.SaveChanges();

        _customerId = customer.Id;
    }

    /// <summary>Adds an entry directly, so the tests are about application rather than posting.</summary>
    private Guid Entry(string documentNo, decimal amount, DateOnly postingDate, GlDocumentType type)
    {
        using var context = NewContext();

        var entry = new CustomerLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PartyId = _customerId,
            PartyNo = "C-0001",
            PartyName = "Al Faisaliah Trading",
            PostingDate = postingDate,
            DueDate = postingDate.AddDays(30),
            TransactionNo = 1,
            DocumentType = type,
            DocumentNo = documentNo,
            Description = documentNo,
            Amount = amount,
            RemainingAmount = amount,
            IsOpen = true,
            ControlAccountNo = "1300",
            SourceCode = "TEST",
        };

        context.Set<CustomerLedgerEntry>().Add(entry);
        context.SaveChanges();

        return entry.Id;
    }

    /// <summary>
    /// Adds an entry written in a foreign currency, at a stated rate.
    /// </summary>
    /// <remarks>
    /// The rate is applied here rather than looked up, because these tests are about what
    /// settlement does with two entries raised at two rates -- not about where the rates came
    /// from, which is the exchange rate service's own business.
    /// </remarks>
    private Guid ForeignEntry(
        string documentNo,
        decimal amountInCurrency,
        decimal rate,
        GlDocumentType type)
    {
        using var context = NewContext();

        var baseAmount = Math.Round(amountInCurrency * rate, 2, MidpointRounding.AwayFromZero);

        var entry = new CustomerLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PartyId = _customerId,
            PartyNo = "C-0001",
            PartyName = "Al Faisaliah Trading",
            PostingDate = Today,
            DueDate = Today.AddDays(30),
            TransactionNo = 1,
            DocumentType = type,
            DocumentNo = documentNo,
            Description = documentNo,
            Amount = baseAmount,
            RemainingAmount = baseAmount,
            AmountInCurrency = amountInCurrency,
            RemainingAmountInCurrency = amountInCurrency,
            CurrencyCode = "USD",
            IsOpen = true,
            ControlAccountNo = "1300",
            SourceCode = "TEST",
        };

        context.Set<CustomerLedgerEntry>().Add(entry);
        context.SaveChanges();

        return entry.Id;
    }

    private Guid Invoice(string no, decimal amount, DateOnly? postingDate = null)
        => Entry(no, amount, postingDate ?? Today, GlDocumentType.Invoice);

    private Guid Payment(string no, decimal amount, DateOnly? postingDate = null)
        => Entry(no, -amount, postingDate ?? Today, GlDocumentType.Payment);

    [Fact]
    public async Task A_foreign_invoice_paid_in_full_closes_even_though_the_rate_moved()
    {
        // USD 1,000 invoiced at 3.75 and paid at 3.80. The customer owes nothing: they were
        // billed a thousand dollars and they sent a thousand dollars.
        var invoice = ForeignEntry("INV-USD-1", 1_000m, 3.75m, GlDocumentType.Invoice);
        var payment = ForeignEntry("PAY-USD-1", -1_000m, 3.80m, GlDocumentType.Payment);

        await using var context = NewContext();
        var exchange = new FakeExchange();

        var result = await Applications(context, exchange)
            .ApplyAsync(PartyKind.Customer, payment, invoice);

        result.Succeeded.ShouldBeTrue();
        result.Value.ClosedEntries.ShouldBe(2, "a thousand dollars settles a thousand dollars");

        var settledInvoice = await ReadAsync(invoice);
        settledInvoice.IsOpen.ShouldBeFalse();
        settledInvoice.RemainingAmountInCurrency.ShouldBe(0m);

        // The riyal amounts did not cancel, and that residue is the whole point: it is not an
        // unpaid balance, and it may not be left sitting on the control account either.
        settledInvoice.RemainingAmount.ShouldBe(0m);

        var posted = exchange.Posted.ShouldHaveSingleItem();
        posted.Account.ShouldBe("1300");
        posted.Currency.ShouldBe("USD");

        // The company received 3,800 riyals for something booked at 3,750, so it is 50 better
        // off. Negative is the direction that means a gain.
        posted.Difference.ShouldBe(-50m);
    }

    [Fact]
    public async Task A_rate_that_moved_the_other_way_is_a_loss()
    {
        var invoice = ForeignEntry("INV-USD-2", 1_000m, 3.80m, GlDocumentType.Invoice);
        var payment = ForeignEntry("PAY-USD-2", -1_000m, 3.75m, GlDocumentType.Payment);

        await using var context = NewContext();
        var exchange = new FakeExchange();

        var result = await Applications(context, exchange)
            .ApplyAsync(PartyKind.Customer, payment, invoice);

        result.Succeeded.ShouldBeTrue();
        exchange.Posted.ShouldHaveSingleItem().Difference.ShouldBe(50m);
    }

    [Fact]
    public async Task Settling_a_foreign_entry_against_a_local_one_is_refused()
    {
        var invoice = ForeignEntry("INV-USD-3", 1_000m, 3.75m, GlDocumentType.Invoice);
        var payment = Payment("PAY-SAR-1", 3_750m);

        await using var context = NewContext();
        var exchange = new FakeExchange();

        var result = await Applications(context, exchange)
            .ApplyAsync(PartyKind.Customer, payment, invoice);

        result.Failed.ShouldBeTrue(
            "the two would have to be compared at some rate, and any rate chosen decides a gain "
            + "or a loss nobody agreed to");

        result.Messages.ShouldContain(m => m.Code.Value == "FIN.APPLICATION.DIFFERENT_CURRENCIES");
        exchange.Posted.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_part_payment_in_a_foreign_currency_leaves_the_rest_open_in_that_currency()
    {
        var invoice = ForeignEntry("INV-USD-4", 1_000m, 3.75m, GlDocumentType.Invoice);
        var payment = ForeignEntry("PAY-USD-4", -400m, 3.80m, GlDocumentType.Payment);

        await using var context = NewContext();
        var exchange = new FakeExchange();

        var result = await Applications(context, exchange)
            .ApplyAsync(PartyKind.Customer, payment, invoice);

        result.Succeeded.ShouldBeTrue();

        var settled = await ReadAsync(invoice);
        settled.IsOpen.ShouldBeTrue();

        // Six hundred dollars still owed, and the riyal figure that goes with it is what those
        // six hundred were worth on the day the invoice was raised -- not on the day of the part
        // payment, which has nothing to say about the part not yet paid.
        settled.RemainingAmountInCurrency.ShouldBe(600m);
        settled.RemainingAmount.ShouldBe(2_250m);

        // 400 came off the payment at 3.80 and off the invoice at 3.75.
        exchange.Posted.ShouldHaveSingleItem().Difference.ShouldBe(-20m);
    }

    [Fact]
    public async Task Unapplying_a_foreign_settlement_gives_both_sides_back_what_it_took()
    {
        var invoice = ForeignEntry("INV-USD-5", 1_000m, 3.75m, GlDocumentType.Invoice);
        var payment = ForeignEntry("PAY-USD-5", -1_000m, 3.80m, GlDocumentType.Payment);

        Guid applicationId;

        await using (var context = NewContext())
        {
            await Applications(context, new FakeExchange())
                .ApplyAsync(PartyKind.Customer, payment, invoice);
        }

        await using (var context = NewContext())
        {
            applicationId = context.Set<CustomerApplication>().Single().Id;
        }

        await using (var context = NewContext())
        {
            var undone = await Applications(context, new FakeExchange())
                .UnapplyAsync(PartyKind.Customer, applicationId);

            undone.Succeeded.ShouldBeTrue();
        }

        // Each side gets back its own figure, which for a foreign pair are two different numbers.
        // Giving both sides the same one would leave the ledger fifty riyals out.
        var restoredInvoice = await ReadAsync(invoice);
        restoredInvoice.RemainingAmount.ShouldBe(3_750m);
        restoredInvoice.RemainingAmountInCurrency.ShouldBe(1_000m);
        restoredInvoice.IsOpen.ShouldBeTrue();

        var restoredPayment = await ReadAsync(payment);
        restoredPayment.RemainingAmount.ShouldBe(-3_800m);
        restoredPayment.RemainingAmountInCurrency.ShouldBe(-1_000m);
        restoredPayment.IsOpen.ShouldBeTrue();
    }

    private async Task<CustomerLedgerEntry> ReadAsync(Guid id)
    {
        await using var context = NewContext();

        return await context.Set<CustomerLedgerEntry>().AsNoTracking().SingleAsync(e => e.Id == id);
    }

    [Fact]
    public async Task A_payment_that_covers_an_invoice_closes_both()
    {
        var invoice = Invoice("INV-001", 1_000m);
        var payment = Payment("PAY-001", 1_000m);

        await using (var context = NewContext())
        {
            var result = await Applications(context)
                .ApplyAsync(PartyKind.Customer, payment, invoice);

            result.Succeeded.ShouldBeTrue();
            result.Value.AppliedAmount.ShouldBe(1_000m);
            result.Value.ClosedEntries.ShouldBe(2);
        }

        (await ReadAsync(invoice)).IsOpen.ShouldBeFalse();
        (await ReadAsync(payment)).IsOpen.ShouldBeFalse();
        (await ReadAsync(invoice)).ClosedOn.ShouldBe(Today);
    }

    [Fact]
    public async Task A_part_payment_leaves_the_invoice_open_for_the_rest()
    {
        var invoice = Invoice("INV-002", 1_000m);
        var payment = Payment("PAY-002", 400m);

        await using (var context = NewContext())
        {
            var result = await Applications(context)
                .ApplyAsync(PartyKind.Customer, payment, invoice);

            result.Succeeded.ShouldBeTrue();
            result.Value.AppliedAmount.ShouldBe(400m, "no more than the payment carried");
        }

        var remaining = await ReadAsync(invoice);

        remaining.RemainingAmount.ShouldBe(600m);
        remaining.IsOpen.ShouldBeTrue();
        remaining.AppliedAmount.ShouldBe(400m);

        // The payment is spent, so it closes even though the invoice does not.
        (await ReadAsync(payment)).IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task One_payment_can_be_spread_across_two_invoices()
    {
        var first = Invoice("INV-003", 300m);
        var second = Invoice("INV-004", 500m);
        var payment = Payment("PAY-003", 1_000m);

        await using (var context = NewContext())
        {
            var applications = Applications(context);

            (await applications.ApplyAsync(PartyKind.Customer, payment, first)).Succeeded.ShouldBeTrue();
            (await applications.ApplyAsync(PartyKind.Customer, payment, second)).Succeeded.ShouldBeTrue();
        }

        (await ReadAsync(first)).IsOpen.ShouldBeFalse();
        (await ReadAsync(second)).IsOpen.ShouldBeFalse();

        // Two hundred left on account, still available for the next invoice. Closing the payment
        // here and writing the balance off is how an overpayment quietly disappears.
        var payer = await ReadAsync(payment);

        payer.RemainingAmount.ShouldBe(-200m);
        payer.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task Unapplying_gives_both_sides_back_exactly_what_was_taken()
    {
        var invoice = Invoice("INV-005", 1_000m);
        var payment = Payment("PAY-005", 400m);

        Guid applicationId;

        await using (var context = NewContext())
        {
            (await Applications(context).ApplyAsync(PartyKind.Customer, payment, invoice))
                .Succeeded.ShouldBeTrue();

            applicationId = await context.Set<CustomerApplication>().Select(a => a.Id).SingleAsync();
        }

        await using (var context = NewContext())
        {
            var result = await Applications(context).UnapplyAsync(PartyKind.Customer, applicationId);

            result.Succeeded.ShouldBeTrue();
        }

        var invoiceAfter = await ReadAsync(invoice);
        var paymentAfter = await ReadAsync(payment);

        invoiceAfter.RemainingAmount.ShouldBe(1_000m);
        invoiceAfter.IsOpen.ShouldBeTrue();
        invoiceAfter.ClosedOn.ShouldBeNull();

        paymentAfter.RemainingAmount.ShouldBe(-400m);
        paymentAfter.IsOpen.ShouldBeTrue();

        await using (var context = NewContext())
        {
            // The row stays, marked. A statement should still be able to say an application was
            // made and then withdrawn.
            var application = await context.Set<CustomerApplication>().AsNoTracking().SingleAsync();

            application.IsReversed.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Two_invoices_cannot_settle_each_other()
    {
        var first = Invoice("INV-006", 100m);
        var second = Invoice("INV-007", 100m);

        await using var context = NewContext();

        var result = await Applications(context).ApplyAsync(PartyKind.Customer, first, second);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code.Value == "FIN.APPLICATION.SAME_DIRECTION");
    }

    [Fact]
    public async Task More_than_is_outstanding_is_refused_rather_than_clamped()
    {
        // Silently applying only what fits would leave the user believing the whole payment was
        // used. Refusing says which figure was wrong.
        var invoice = Invoice("INV-008", 100m);
        var payment = Payment("PAY-008", 500m);

        await using var context = NewContext();

        var result = await Applications(context)
            .ApplyAsync(PartyKind.Customer, payment, invoice, amount: 200m);

        result.Failed.ShouldBeTrue();

        var message = result.Messages.ShouldHaveSingleItem();

        message.Code.Value.ShouldBe("FIN.APPLICATION.TOO_LARGE");
        message.Resolution.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task An_entry_already_settled_cannot_be_applied_against()
    {
        var invoice = Invoice("INV-009", 100m);
        var payment = Payment("PAY-009", 100m);
        var second = Payment("PAY-010", 50m);

        await using (var context = NewContext())
        {
            (await Applications(context).ApplyAsync(PartyKind.Customer, payment, invoice))
                .Succeeded.ShouldBeTrue();
        }

        await using (var context = NewContext())
        {
            var result = await Applications(context).ApplyAsync(PartyKind.Customer, second, invoice);

            result.Failed.ShouldBeTrue();
            result.Messages.ShouldContain(m => m.Code.Value == "FIN.APPLICATION.ENTRY_CLOSED");
        }
    }

    [Fact]
    public async Task Ageing_uses_the_due_date_not_the_posting_date()
    {
        // Raised sixty days ago on thirty-day terms, so thirty days overdue -- not sixty. A report
        // that ages from the posting date calls everything overdue and gets ignored.
        Invoice("INV-010", 500m, Today.AddDays(-60));

        await using var context = NewContext();

        var report = await new AgedAnalysisQueryHandler(context, _clock)
            .HandleAsync(new AgedAnalysisQuery(PartyKind.Customer, Today));

        var row = report.Rows.ShouldHaveSingleItem();

        row.OldestDaysOverdue.ShouldBe(30);
        row.Total.ShouldBe(500m);

        // Columns are NotDue, 1-30, 31-60, 61-90, Over90.
        report.BandLabels.ShouldBe(["NotDue", "1-30", "31-60", "61-90", "Over90"]);
        row.Buckets[1].ShouldBe(500m, "thirty days overdue falls in the 1-30 band");
    }

    [Fact]
    public async Task Anything_past_the_last_band_gets_its_own_column()
    {
        // The bug this catches: with bands 30/60/90 it is easy to write the loop so that "over 90"
        // lands back in the 61-90 column, and the report then shows nothing as seriously overdue.
        Invoice("INV-011", 100m, Today.AddDays(-200));
        Invoice("INV-012", 40m, Today.AddDays(-100));
        Invoice("INV-013", 25m);

        await using var context = NewContext();

        var report = await new AgedAnalysisQueryHandler(context, _clock)
            .HandleAsync(new AgedAnalysisQuery(PartyKind.Customer, Today));

        var row = report.Rows.ShouldHaveSingleItem();

        row.Buckets.Count.ShouldBe(5);
        row.Buckets[0].ShouldBe(25m, "not yet due");
        row.Buckets[3].ShouldBe(40m, "seventy days overdue");
        row.Buckets[4].ShouldBe(100m, "a hundred and seventy days overdue");
        row.Total.ShouldBe(165m);
        report.Total.ShouldBe(165m);
    }

    [Fact]
    public async Task Bands_can_be_chosen_to_suit_the_terms_actually_sold_on()
    {
        Invoice("INV-014", 90m, Today.AddDays(-33));

        await using var context = NewContext();

        var report = await new AgedAnalysisQueryHandler(context, _clock)
            .HandleAsync(new AgedAnalysisQuery(PartyKind.Customer, Today, [7, 14]));

        report.BandLabels.ShouldBe(["NotDue", "1-7", "8-14", "Over14"]);

        // Three days overdue against thirty-day terms.
        report.Rows.ShouldHaveSingleItem().Buckets[1].ShouldBe(90m);
    }

    [Fact]
    public async Task Settled_entries_are_left_off_the_chase_list()
    {
        var invoice = Invoice("INV-015", 700m, Today.AddDays(-90));
        var payment = Payment("PAY-015", 700m);

        await using (var context = NewContext())
        {
            (await Applications(context).ApplyAsync(PartyKind.Customer, payment, invoice))
                .Succeeded.ShouldBeTrue();
        }

        await using (var context = NewContext())
        {
            var report = await new AgedAnalysisQueryHandler(context, _clock)
                .HandleAsync(new AgedAnalysisQuery(PartyKind.Customer, Today));

            report.Rows.ShouldBeEmpty();
            report.Total.ShouldBe(0m);
        }
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

    /// <summary>
    /// Records what the application service asked to be posted, and posts nothing.
    /// </summary>
    /// <remarks>
    /// The point of the seam. What is worth checking here is the arithmetic — which account, how
    /// much, and which way round — and none of that needs a chart of accounts, a fiscal calendar
    /// or a transaction number to answer. Whether a two-line journal posts correctly is the
    /// posting engine's own test suite's job.
    /// </remarks>
    private sealed class FakeExchange : IExchangeDifferencePoster
    {
        public List<(string Account, decimal Difference, string? Currency)> Posted { get; } = [];

        public Task<Result<long>> PostAsync(
            string controlAccountNo,
            decimal difference,
            string? currencyCode,
            string? documentNo,
            Guid? branchId,
            CancellationToken cancellationToken = default)
        {
            Posted.Add((controlAccountNo, difference, currencyCode));

            return Task.FromResult(Result<long>.Success(Posted.Count));
        }
    }
}
