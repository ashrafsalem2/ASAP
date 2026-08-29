using ASAP.Modules.Finance.Banking;
using ASAP.Modules.Finance.Ledger;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Banking;

/// <summary>
/// Covers agreeing a bank statement with the ledger, and refusing to say it agrees when it does
/// not.
/// </summary>
/// <remarks>
/// The identity under test is that the books must be ahead of the bank by exactly the items the
/// bank has not seen. Everything worth checking here is a way that can be true or false: a cheque
/// not yet presented (true, and the reconciliation should close), a bank charge nobody posted
/// (false, and it should not), and an entry matched twice (false in a way that looks right until
/// the arithmetic is done).
/// </remarks>
public sealed class BankReconciliationTests : IDisposable
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000b1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000b1");
    private static readonly DateOnly MonthEnd = new(2026, 3, 31);

    private readonly DbContextOptions<AsapDbContext> _options;
    private readonly StubTenant _tenancy = new();
    private readonly StubClock _clock = new(new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc));
    private readonly List<AsapDbContext> _opened = [];

    private Guid _bankAccountId;
    private long _transactionNo = 1;

    public BankReconciliationTests()
    {
        _options = new DbContextOptionsBuilder<AsapDbContext>()
            .UseInMemoryDatabase($"asap-bank-{Guid.CreateVersion7()}")
            .Options;

        using var context = NewContext();

        var account = new BankAccount
        {
            TenantId = Tenant,
            CompanyId = Company,
            Code = "SNB-MAIN",
            Name = "Saudi National Bank — main",
            GlAccountNo = "1100",
        };

        context.Set<BankAccount>().Add(account);
        context.SaveChanges();

        _bankAccountId = account.Id;
    }

    private AsapDbContext NewContext()
    {
        var context = new AsapDbContext(_options, _tenancy, new StubUser(), _clock, [new FinanceSchema()]);
        _opened.Add(context);
        return context;
    }

    private BankReconciliationService Service(AsapDbContext context)
        => new(
            context,
            new MessageCatalog(FinanceMessages.All),
            new StubUser(),
            _clock,
            NullLogger<BankReconciliationService>.Instance);

    /// <summary>Puts an entry on the bank's ledger account.</summary>
    private Guid Entry(string documentNo, decimal amount, DateOnly postingDate)
    {
        using var context = NewContext();

        var entry = new GlEntry
        {
            TenantId = Tenant,
            CompanyId = Company,
            PostingDate = postingDate,
            TransactionNo = _transactionNo++,
            AccountId = Guid.CreateVersion7(),
            AccountNo = "1100",
            DocumentNo = documentNo,
            Description = documentNo,
            Amount = amount,
            DebitAmount = amount > 0 ? amount : 0m,
            CreditAmount = amount < 0 ? -amount : 0m,
            SourceCode = "TEST",
        };

        context.Set<GlEntry>().Add(entry);
        context.SaveChanges();

        return entry.Id;
    }

    /// <summary>Enters a statement, with its lines on the ledger's sign convention.</summary>
    private Guid Statement(
        string no,
        decimal opening,
        decimal closing,
        params (string Description, decimal Amount, DateOnly On)[] lines)
    {
        using var context = NewContext();

        var statement = new BankStatement
        {
            TenantId = Tenant,
            CompanyId = Company,
            BankAccountId = _bankAccountId,
            No = no,
            StatementDate = MonthEnd,
            OpeningBalance = opening,
            ClosingBalance = closing,
        };

        context.Set<BankStatement>().Add(statement);

        foreach (var (description, amount, on) in lines)
        {
            context.Set<BankStatementLine>().Add(new BankStatementLine
            {
                TenantId = Tenant,
                CompanyId = Company,
                BankStatementId = statement.Id,
                TransactionDate = on,
                Description = description,
                Amount = amount,
            });
        }

        context.SaveChanges();

        return statement.Id;
    }

    [Fact]
    public async Task A_cheque_not_yet_presented_is_outstanding_and_the_statement_still_closes()
    {
        // The books: two receipts the bank has seen, and a cheque written on the last day that
        // will not clear until April.
        var receipt = Entry("RCP-1", 10_000m, new DateOnly(2026, 3, 5));
        var transfer = Entry("RCP-2", 4_000m, new DateOnly(2026, 3, 18));
        Entry("CHQ-9", -1_500m, new DateOnly(2026, 3, 31));

        // The bank saw 14,000 of movement and knows nothing of the cheque.
        var statementId = Statement(
            "MAR-2026",
            0m,
            14_000m,
            ("Deposit", 10_000m, new DateOnly(2026, 3, 5)),
            ("Transfer in", 4_000m, new DateOnly(2026, 3, 18)));

        await using var context = NewContext();
        var service = Service(context);

        var lines = await context.Set<BankStatementLine>()
            .Where(l => l.BankStatementId == statementId)
            .OrderBy(l => l.TransactionDate)
            .Select(l => l.Id)
            .ToListAsync();

        (await service.MatchAsync(lines[0], receipt)).Succeeded.ShouldBeTrue();
        (await service.MatchAsync(lines[1], transfer)).Succeeded.ShouldBeTrue();

        var result = await service.ReconcileAsync(statementId);

        result.Succeeded.ShouldBeTrue("the only difference is a cheque the bank has not seen");

        result.Value.LedgerBalance.ShouldBe(12_500m);
        result.Value.ClosingBalance.ShouldBe(14_000m);
        result.Value.OutstandingTotal.ShouldBe(-1_500m);
        result.Value.Difference.ShouldBe(0m);
        result.Value.Outstanding.ShouldHaveSingleItem().DocumentNo.ShouldBe("CHQ-9");
    }

    [Fact]
    public async Task A_bank_charge_nobody_posted_stops_the_statement_closing()
    {
        var receipt = Entry("RCP-1", 10_000m, new DateOnly(2026, 3, 5));

        // The bank took 45 in charges. Nothing in the books knows about it, which is exactly the
        // thing a reconciliation exists to find.
        var statementId = Statement(
            "MAR-2026",
            0m,
            9_955m,
            ("Deposit", 10_000m, new DateOnly(2026, 3, 5)),
            ("Account charges", -45m, new DateOnly(2026, 3, 31)));

        await using var context = NewContext();
        var service = Service(context);

        var lines = await context.Set<BankStatementLine>()
            .Where(l => l.BankStatementId == statementId)
            .OrderBy(l => l.TransactionDate)
            .Select(l => l.Id)
            .ToListAsync();

        await service.MatchAsync(lines[0], receipt);

        var result = await service.ReconcileAsync(statementId);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code.Value == "FIN.BANK.LINES_UNMATCHED");

        // And it says by how much, which is the number that identifies the charge.
        var gap = result.Messages.Single(m => m.Code.Value == "FIN.BANK.DOES_NOT_BALANCE");
        gap.Detail.ShouldNotBeNull().ShouldContain("45");
    }

    [Fact]
    public async Task A_statement_that_does_not_agree_with_itself_says_so_before_anything_else()
    {
        Entry("RCP-1", 10_000m, new DateOnly(2026, 3, 5));

        // Closing says 9,000 but the single line says 10,000. The error is inside the statement.
        var statementId = Statement(
            "MAR-2026",
            0m,
            9_000m,
            ("Deposit", 10_000m, new DateOnly(2026, 3, 5)));

        await using var context = NewContext();

        var result = await Service(context).ReconcileAsync(statementId);

        result.Failed.ShouldBeTrue();

        var refusal = result.Messages.Single(m => m.Code.Value == "FIN.BANK.LINES_DO_NOT_ADD_UP");
        refusal.Detail.ShouldNotBeNull().ShouldContain("1,000.00");
    }

    [Fact]
    public async Task One_entry_cannot_settle_two_lines()
    {
        var receipt = Entry("RCP-1", 5_000m, new DateOnly(2026, 3, 5));

        var statementId = Statement(
            "MAR-2026",
            0m,
            10_000m,
            ("Deposit", 5_000m, new DateOnly(2026, 3, 5)),
            ("Deposit", 5_000m, new DateOnly(2026, 3, 12)));

        await using var context = NewContext();
        var service = Service(context);

        var lines = await context.Set<BankStatementLine>()
            .Where(l => l.BankStatementId == statementId)
            .OrderBy(l => l.TransactionDate)
            .Select(l => l.Id)
            .ToListAsync();

        (await service.MatchAsync(lines[0], receipt)).Succeeded.ShouldBeTrue();

        // The same money cannot have cleared twice, and letting it would hide a real difference
        // of exactly the same size.
        var second = await service.MatchAsync(lines[1], receipt);

        second.Failed.ShouldBeTrue();
        second.Messages.ShouldContain(m => m.Code.Value == "FIN.BANK.ENTRY_ALREADY_MATCHED");
    }

    [Fact]
    public async Task An_entry_on_another_account_is_refused_however_well_the_amount_fits()
    {
        using (var seed = NewContext())
        {
            seed.Set<GlEntry>().Add(new GlEntry
            {
                TenantId = Tenant,
                CompanyId = Company,
                PostingDate = new DateOnly(2026, 3, 5),
                TransactionNo = 99,
                AccountId = Guid.CreateVersion7(),
                AccountNo = "1150",
                DocumentNo = "OTHER-1",
                Description = "Petty cash",
                Amount = 500m,
                DebitAmount = 500m,
                SourceCode = "TEST",
            });

            seed.SaveChanges();
        }

        var statementId = Statement("MAR-2026", 0m, 500m, ("Deposit", 500m, new DateOnly(2026, 3, 5)));

        await using var context = NewContext();
        var service = Service(context);

        var otherEntry = await context.Set<GlEntry>().Where(e => e.AccountNo == "1150").Select(e => e.Id).SingleAsync();
        var line = await context.Set<BankStatementLine>().Where(l => l.BankStatementId == statementId).Select(l => l.Id).SingleAsync();

        var result = await service.MatchAsync(line, otherEntry);

        result.Failed.ShouldBeTrue();
        result.Messages.ShouldContain(m => m.Code.Value == "FIN.BANK.ENTRY_WRONG_ACCOUNT");
    }

    [Fact]
    public async Task Matching_an_amount_that_differs_warns_and_is_still_recorded()
    {
        // One bank line covering two payments, which is ordinary.
        var first = Entry("PAY-1", -600m, new DateOnly(2026, 3, 10));
        Entry("PAY-2", -400m, new DateOnly(2026, 3, 10));

        var statementId = Statement(
            "MAR-2026", 0m, -1_000m, ("Supplier run", -1_000m, new DateOnly(2026, 3, 10)));

        await using var context = NewContext();
        var service = Service(context);

        var line = await context.Set<BankStatementLine>()
            .Where(l => l.BankStatementId == statementId)
            .Select(l => l.Id)
            .SingleAsync();

        var result = await service.MatchAsync(line, first);

        result.Succeeded.ShouldBeTrue("somebody has to be able to record a part match");
        result.Messages.ShouldContain(m => m.Code.Value == "FIN.BANK.MATCH_AMOUNT_DIFFERS");

        // Warned, not waved through: the arithmetic is what protects the result, and the second
        // payment is still sitting there unaccounted for.
        var reconciled = await service.ReconcileAsync(statementId);

        reconciled.Failed.ShouldBeTrue();
        reconciled.Messages.Single(m => m.Code.Value == "FIN.BANK.DOES_NOT_BALANCE")
            .Detail.ShouldNotBeNull().ShouldContain("400");
    }

    [Fact]
    public async Task An_agreed_statement_cannot_be_unpicked()
    {
        var receipt = Entry("RCP-1", 1_000m, new DateOnly(2026, 3, 5));
        var statementId = Statement("MAR-2026", 0m, 1_000m, ("Deposit", 1_000m, new DateOnly(2026, 3, 5)));

        await using var context = NewContext();
        var service = Service(context);

        var line = await context.Set<BankStatementLine>()
            .Where(l => l.BankStatementId == statementId)
            .Select(l => l.Id)
            .SingleAsync();

        await service.MatchAsync(line, receipt);
        (await service.ReconcileAsync(statementId)).Succeeded.ShouldBeTrue();

        // Every later reconciliation is measured against this one, so unpicking it would make
        // those wrong without anybody being told.
        var undo = await service.UnmatchAsync(line);

        undo.Failed.ShouldBeTrue();
        undo.Messages.ShouldContain(m => m.Code.Value == "FIN.BANK.ALREADY_RECONCILED");
    }

    [Fact]
    public async Task Suggestions_leave_the_ambiguous_ones_alone()
    {
        // One clear match, and two entries of the same amount within the window that a machine
        // has no business choosing between.
        var rent = Entry("RENT-3", -7_500m, new DateOnly(2026, 3, 1));
        Entry("FLOAT-1", 2_000m, new DateOnly(2026, 3, 9));
        Entry("FLOAT-2", 2_000m, new DateOnly(2026, 3, 11));

        var statementId = Statement(
            "MAR-2026",
            0m,
            -3_500m,
            ("Rent", -7_500m, new DateOnly(2026, 3, 1)),
            ("Cash in", 2_000m, new DateOnly(2026, 3, 10)),
            ("Cash in", 2_000m, new DateOnly(2026, 3, 12)));

        await using var context = NewContext();

        var suggestions = await Service(context).SuggestAsync(statementId);

        suggestions.ShouldHaveSingleItem().EntryId.ShouldBe(rent);
    }

    public void Dispose()
    {
        foreach (var context in _opened)
        {
            context.Dispose();
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
        public Guid? UserId => Guid.Parse("eeeeeeee-0000-0000-0000-0000000000b1");

        public string? UserName => "tests";

        public string? DisplayName => "Tests";

        public string? Culture => "en";

        public bool IsSuperUser => true;

        public IReadOnlySet<string> Permissions => new HashSet<string>();

        public bool Has(string permissionKey) => true;

        public Guid RequireUserId() => UserId ?? Guid.Empty;
    }

    private sealed class StubClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;

        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }
}
