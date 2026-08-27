using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Finance.Posting;
using ASAP.Modules.Finance.Periods;
using ASAP.Modules.Finance.Tax;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Posting;

/// <summary>
/// A reversal has to reach every ledger the original wrote to, not just the general ledger.
/// </summary>
/// <remarks>
/// It reached only the general ledger for a while, and the consequences were specific rather than
/// theoretical: a customer stayed on the aged analysis for an invoice that had been cancelled, and
/// the tax on it stayed on the return and would have been declared and paid to the authority for a
/// sale that never happened.
/// </remarks>
public sealed class ReversalReachesEveryLedgerTests : IDisposable
{
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 8, 27);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 8, 27, 9, 0, 0, DateTimeKind.Utc));
    private readonly CountingAllocator _allocator = new();
    private readonly List<AsapDbContext> _opened = [];

    private Guid _customerId;

    public ReversalReachesEveryLedgerTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-reversal-{Guid.CreateVersion7()}")
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

        var customer = new Customer
        {
            TenantId = Tenant,
            CompanyId = Company,
            No = "C-0001",
            Name = "Al Faisaliah Trading",
            PaymentTermsDays = 30,
        };

        context.Set<Customer>().Add(customer);

        context.Set<FiscalYear>().Add(new FiscalYear
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "2026",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            Periods =
            {
                new FiscalPeriod
                {
                    TenantId = Tenant,
                    CompanyId = Company,
                    PeriodNo = 8,
                    Name = "August",
                    StartDate = new DateOnly(2026, 8, 1),
                    EndDate = new DateOnly(2026, 8, 31),
                },
            },
        });

        context.SaveChanges();
        _customerId = customer.Id;
    }

    /// <summary>Writes a taxable sale directly, so the test is about reversing rather than posting.</summary>
    private void SellOnCredit(long transactionNo)
    {
        using var context = NewContext();

        void Gl(string accountNo, decimal amount)
            => context.Set<GlEntry>().Add(new GlEntry
            {
                TenantId = Tenant,
                CompanyId = Company,
                AccountNo = accountNo,
                PostingDate = Today,
                TransactionNo = transactionNo,
                Amount = amount,
                DebitAmount = amount > 0 ? amount : 0m,
                CreditAmount = amount < 0 ? -amount : 0m,
                DocumentNo = "INV-001",
                Description = "Consultancy",
                SourceCode = "GENJNL",
            });

        Gl("1300", 2_300m);
        Gl("4100", -2_000m);
        Gl("2200", -300m);

        context.Set<CustomerLedgerEntry>().Add(new CustomerLedgerEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PartyId = _customerId,
            PartyNo = "C-0001",
            PartyName = "Al Faisaliah Trading",
            PostingDate = Today,
            DueDate = Today.AddDays(30),
            TransactionNo = transactionNo,
            DocumentNo = "INV-001",
            Description = "Consultancy",
            Amount = 2_300m,
            RemainingAmount = 2_300m,
            IsOpen = true,
            ControlAccountNo = "1300",
            SourceCode = "GENJNL",
        });

        context.Set<TaxEntry>().Add(new TaxEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PostingDate = Today,
            TransactionNo = transactionNo,
            Direction = TaxDirection.Output,
            TaxCodeNo = "VAT",
            Kind = TaxKind.Standard,
            Percentage = 15m,
            BaseAmount = 2_000m,
            TaxAmount = 300m,
            DocumentNo = "INV-001",
            PartyNo = "C-0001",
            SourceCode = "GENJNL",
        });

        var customer = context.Set<Customer>().Single();
        customer.Balance = 2_300m;

        context.SaveChanges();
    }

    private async Task ReverseAsync(long transactionNo)
    {
        await using var context = NewContext();

        var handler = new ReverseTransactionCommandHandler(
            context,
            new MessageCatalog(FinanceMessages.All),
            _tenancy,
            new StubUser(),
            _clock,
            _allocator,
            NullLogger<ReverseTransactionCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new ReverseTransactionCommand(transactionNo, Reason: "Customer cancelled"));

        result.Succeeded.ShouldBeTrue(
            string.Join(", ", result.Messages.Select(m => m.Code.Value + ": " + m.Detail)));
    }

    [Fact]
    public async Task Reversing_a_sale_takes_it_off_the_customer_account()
    {
        SellOnCredit(100);
        await ReverseAsync(100);

        await using var context = NewContext();

        var entries = await context.Set<CustomerLedgerEntry>().AsNoTracking().ToListAsync();

        entries.Count.ShouldBe(2, "the original stays and the reversal is added beside it");

        var reversal = entries.Single(e => e.TransactionNo != 100);

        reversal.Amount.ShouldBe(-2_300m, "the whole gross, not just the net");
        reversal.IsOpen.ShouldBeTrue();

        // Due the day it is raised. Carrying the original's due date would put a cancellation
        // into an overdue bucket on the aged analysis, where it reads as something to chase.
        reversal.DueDate.ShouldBe(Today);

        // And the balance the list screen reads comes back to nothing.
        (await context.Set<Customer>().AsNoTracking().SingleAsync()).Balance.ShouldBe(0m);
    }

    [Fact]
    public async Task Reversing_a_sale_takes_its_tax_off_the_return()
    {
        // The consequence of getting this wrong is not an untidy report: it is declaring and
        // paying tax on a sale that never happened.
        SellOnCredit(200);
        await ReverseAsync(200);

        await using var context = NewContext();

        var entries = await context.Set<TaxEntry>().AsNoTracking().ToListAsync();

        entries.Count.ShouldBe(2);
        entries.Sum(static e => e.TaxAmount).ShouldBe(0m);
        entries.Sum(static e => e.BaseAmount).ShouldBe(0m);

        var reversal = entries.Single(e => e.TransactionNo != 200);

        // The rate the original carried, not today's. A reversal in a later year still has to
        // offset exactly what was charged at the time.
        reversal.Percentage.ShouldBe(15m);
        reversal.TaxCodeNo.ShouldBe("VAT");
        reversal.PartyNo.ShouldBe("C-0001");
    }

    [Fact]
    public async Task The_original_entries_are_flagged_but_never_edited()
    {
        SellOnCredit(300);
        await ReverseAsync(300);

        await using var context = NewContext();

        var originals = await context.Set<GlEntry>()
            .AsNoTracking()
            .Where(e => e.TransactionNo == 300)
            .ToListAsync();

        originals.ShouldAllBe(e => e.IsReversed);
        originals.ShouldAllBe(e => e.ReversedByEntryId != null);

        // Amounts, dates and descriptions stay exactly as posted. An audit trail that can be
        // tidied up is not an audit trail.
        originals.Sum(static e => e.Amount).ShouldBe(0m);
        originals.Single(e => e.AccountNo == "1300").Amount.ShouldBe(2_300m);
        originals.ShouldAllBe(e => e.PostingDate == Today);

        // The customer entry it wrote is left open too, rather than being applied against its own
        // reversal: the original may already carry a part payment, and unpicking that afterwards
        // is worse than matching two obvious entries by hand.
        var customerEntries = await context.Set<CustomerLedgerEntry>().AsNoTracking().ToListAsync();

        customerEntries.ShouldAllBe(e => e.IsOpen);
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

    private sealed class CountingAllocator : ITransactionNumberAllocator
    {
        private long _last = 9_000;

        public Task<long> NextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(++_last);
    }
}
