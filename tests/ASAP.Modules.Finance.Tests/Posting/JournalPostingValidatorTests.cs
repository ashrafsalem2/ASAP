using ASAP.Modules.Finance;
using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Core.Messaging;
using ASAP.Platform.Kernel.Messaging;
using Shouldly;

namespace ASAP.Modules.Finance.Tests.Posting;

public sealed class JournalPostingValidatorTests
{
    private static readonly Guid Department = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SalesDept = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateOnly OpenDate = new(2026, 8, 26);

    private static readonly MandatoryDimensionView RequiredDepartment =
        new(Department, "DEPARTMENT", "Department");

    private static readonly MandatoryDimensionView OptionalDepartment =
        new(Department, "DEPARTMENT", "Department", IsCompanyWide: false);

    private static JournalPostingValidator Validator()
        => new(new MessageCatalog([.. PlatformMessages.All, .. FinanceMessages.All]));

    /// <summary>Finds a message by code, reporting what was actually raised when it is absent.</summary>
    private static AsapMessage Find(ASAP.Platform.Kernel.Results.Result result, string code)
    {
        var match = result.Messages.FirstOrDefault(m => m.Code.Value == code);

        match.ShouldNotBeNull(
            $"Expected a {code} message. Raised: "
            + string.Join(", ", result.Messages.Select(static m => m.Code.Value)));

        return match;
    }

    private static PostingAccountView Account(
        string no = "5100",
        string name = "Office expenses",
        GlAccountType type = GlAccountType.Posting,
        bool blocked = false,
        bool allowsDirect = true,
        IReadOnlySet<Guid>? requiredDimensions = null)
        => new(Guid.CreateVersion7(), no, name, type, blocked, allowsDirect, requiredDimensions);

    private static PostingEnvironment Environment(
        Func<DateOnly, PeriodStatus>? periods = null,
        DateOnly? windowFrom = null,
        DateOnly? windowTo = null,
        IReadOnlyList<MandatoryDimensionView>? mandatory = null,
        IReadOnlySet<string>? overrides = null,
        bool manual = true)
        => new(
            BatchCode: "DEFAULT",
            CurrencyCode: "SAR",
            ResolvePeriod: periods ?? (_ => PeriodStatus.Open("August 2026", "2026")),
            CurrencyDecimals: 2,
            PostingWindowFrom: windowFrom,
            PostingWindowTo: windowTo,
            MandatoryDimensions: mandatory,
            HeldOverridePermissions: overrides,
            IsManualEntry: manual);

    private static PostingLineView Line(
        int lineNo,
        decimal amount,
        PostingAccountView? account = null,
        PostingAccountView? balancing = null,
        DateOnly? date = null,
        DimensionCombination dimensions = default)
        => new(
            lineNo,
            date ?? OpenDate,
            amount,
            account ?? Account(),
            balancing,
            dimensions);

    private static DimensionCombination WithDepartment()
        => DimensionCombination.From([new DimensionPair(Department, SalesDept)]);

    [Fact]
    public void Accepts_a_balanced_pair_of_lines()
    {
        var result = Validator().Validate(
            [Line(1, 500m), Line(2, -500m)],
            Environment());

        result.Succeeded.ShouldBeTrue();
        result.Messages.ShouldBeEmpty();
    }

    [Fact]
    public void Refuses_a_journal_that_does_not_balance_and_says_by_how_much()
    {
        var result = Validator().Validate(
            [Line(1, 5150m), Line(2, -5000m)],
            Environment());

        result.Failed.ShouldBeTrue();
        var failure = result.Failures.ShouldHaveSingleItem();
        failure.Code.Value.ShouldBe("FIN.JOURNAL.OUT_OF_BALANCE");
        failure.Detail.ShouldNotBeNull().ShouldContain("150.00 SAR");
        failure.Resolution.ShouldNotBeNull().ShouldContain("150.00");
        failure.Arguments["Difference"].ShouldBe(150m);
    }

    [Fact]
    public void A_line_with_a_balancing_account_stands_alone()
    {
        // It produces two entries that net to nothing, so it must not be counted again in the
        // batch total. Counting it would make every single-line payment journal look unbalanced.
        var result = Validator().Validate(
            [Line(1, 500m, balancing: Account("1100", "Bank"))],
            Environment());

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Rounds_to_currency_precision_before_judging_the_balance()
    {
        // Three lines each a third of a fils out would pass a raw-decimal comparison and post a
        // ledger that does not balance once the figures are stored at two decimals.
        var result = Validator().Validate(
            [Line(1, 100.004m), Line(2, -100.001m)],
            Environment());

        // Both round to 100.00, so the batch balances on the figures that will be stored.
        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Catches_a_difference_that_survives_rounding()
    {
        var result = Validator().Validate(
            [Line(1, 100.006m), Line(2, -100.001m)],
            Environment());

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().Code.Value.ShouldBe("FIN.JOURNAL.OUT_OF_BALANCE");
    }

    [Fact]
    public void Refuses_an_empty_batch()
    {
        var result = Validator().Validate([], Environment());

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldHaveSingleItem().Code.Value.ShouldBe("FIN.JOURNAL.BATCH_EMPTY");
    }

    [Fact]
    public void Refuses_a_line_with_no_account()
    {
        var result = Validator().Validate(
            [new PostingLineView(1, OpenDate, 500m, null), Line(2, -500m)],
            Environment());

        result.Failures.ShouldContain(m => m.Code.Value == "FIN.JOURNAL.ACCOUNT_MISSING");
    }

    [Theory]
    [InlineData(GlAccountType.Heading)]
    [InlineData(GlAccountType.Total)]
    [InlineData(GlAccountType.BeginTotal)]
    [InlineData(GlAccountType.EndTotal)]
    public void Refuses_posting_to_an_account_that_only_shapes_the_report(GlAccountType type)
    {
        // An entry on a totalling account is counted twice: once as itself and once inside the
        // range it sums. The chart then stops adding up, and nothing says why.
        var result = Validator().Validate(
            [Line(1, 500m, Account("4000", "Revenue", type)), Line(2, -500m)],
            Environment());

        result.Failures.ShouldContain(m => m.Code.Value == "FIN.ACCOUNT.NOT_POSTABLE");
    }

    [Fact]
    public void Refuses_a_blocked_account()
    {
        var result = Validator().Validate(
            [Line(1, 500m, Account("5199", "Old expenses", blocked: true)), Line(2, -500m)],
            Environment());

        result.Failures.ShouldContain(m => m.Code.Value == "FIN.ACCOUNT.BLOCKED");
    }

    [Fact]
    public void Refuses_a_person_posting_by_hand_to_a_control_account()
    {
        // A hand-keyed entry to receivables makes the control account disagree with the customer
        // ledger behind it, and that difference is found months later by someone reconciling.
        var result = Validator().Validate(
            [Line(1, 500m, Account("1300", "Accounts receivable", allowsDirect: false)), Line(2, -500m)],
            Environment(manual: true));

        var failure = Find(result, "FIN.ACCOUNT.DIRECT_POSTING_BLOCKED");

        // Overridable, unlike a closed year: sometimes a correction genuinely does belong here,
        // and the resolution names the better route rather than simply refusing.
        failure.Severity.ShouldBe(MessageSeverity.Blocked);
        failure.OverridePermission.ShouldBe("Finance.Account.Override");
        failure.Resolution.ShouldNotBeNull().ShouldContain("reversal");
    }

    [Fact]
    public void Allows_a_module_to_post_to_its_own_control_account()
    {
        // The same account the clerk was refused. Sales posting a receivable is the whole point
        // of the account existing.
        var result = Validator().Validate(
            [Line(1, 500m, Account("1300", "Accounts receivable", allowsDirect: false)), Line(2, -500m)],
            Environment(manual: false));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Refuses_a_line_that_posts_nothing()
    {
        var result = Validator().Validate(
            [Line(1, 0m), Line(2, 0m)],
            Environment());

        result.Failures.Count(static m => m.Code.Value == "FIN.JOURNAL.AMOUNT_ZERO").ShouldBe(2);
    }

    [Fact]
    public void Refuses_a_date_no_period_covers()
    {
        var result = Validator().Validate(
            [Line(1, 500m, date: new DateOnly(2030, 1, 1)), Line(2, -500m)],
            Environment(periods: d => d.Year == 2030
                ? new PeriodStatus(PeriodAvailability.NotDefined)
                : PeriodStatus.Open("August 2026", "2026")));

        var failure = Find(result, "FIN.PERIOD.NOT_DEFINED");
        failure.Resolution.ShouldNotBeNull().ShouldContain("new year has begun");
    }

    [Fact]
    public void Refuses_a_closed_period()
    {
        var result = Validator().Validate(
            [Line(1, 500m), Line(2, -500m)],
            Environment(periods: _ => new PeriodStatus(PeriodAvailability.PeriodClosed, "July 2026", "2026")));

        var failure = Find(result, "FIN.PERIOD.CLOSED");
        failure.Severity.ShouldBe(MessageSeverity.Blocked);
        failure.IsOverridable.ShouldBeTrue();
    }

    [Fact]
    public void Lets_someone_with_the_override_post_to_a_closed_period_but_records_a_warning()
    {
        // The override permission on a message definition earning its place: the posting goes
        // through, and the warning is what the audit log records as an override.
        var result = Validator().Validate(
            [Line(1, 500m), Line(2, -500m)],
            Environment(
                periods: _ => new PeriodStatus(PeriodAvailability.PeriodClosed, "July 2026", "2026"),
                overrides: new HashSet<string> { "Finance.Period.Override" }));

        result.Succeeded.ShouldBeTrue();
        var warning = Find(result, "FIN.PERIOD.CLOSED");
        warning.Severity.ShouldBe(MessageSeverity.Warning);
    }

    [Fact]
    public void Refuses_a_closed_year_to_everyone()
    {
        // Deliberately not overridable at all. The statements have been issued and possibly
        // audited; a late entry would make the filed figures wrong with nothing to show it.
        var result = Validator().Validate(
            [Line(1, 500m), Line(2, -500m)],
            Environment(
                periods: _ => new PeriodStatus(PeriodAvailability.YearClosed, "December 2025", "2025"),
                overrides: new HashSet<string>
                {
                    "Finance.Period.Override",
                    "Finance.Account.Override",
                }));

        var failure = Find(result, "FIN.YEAR.CLOSED");
        failure.Severity.ShouldBe(MessageSeverity.Blocked);
        failure.IsOverridable.ShouldBeFalse();
        failure.Resolution.ShouldNotBeNull().ShouldContain("prior-period adjustment");
    }

    [Fact]
    public void Refuses_a_date_before_the_users_posting_window()
    {
        var result = Validator().Validate(
            [Line(1, 500m, date: new DateOnly(2026, 7, 15)), Line(2, -500m, date: new DateOnly(2026, 8, 1))],
            Environment(windowFrom: new DateOnly(2026, 8, 1)));

        result.Failures.ShouldContain(m => m.Code.Value == "FIN.PERIOD.OUTSIDE_POSTING_WINDOW");
    }

    [Fact]
    public void Refuses_a_date_after_the_users_posting_window()
    {
        var result = Validator().Validate(
            [Line(1, 500m, date: new DateOnly(2026, 9, 15)), Line(2, -500m)],
            Environment(windowTo: new DateOnly(2026, 8, 31)));

        result.Failures.ShouldContain(m => m.Code.Value == "FIN.PERIOD.OUTSIDE_POSTING_WINDOW");
    }

    [Fact]
    public void Accepts_a_date_inside_the_posting_window()
    {
        var result = Validator().Validate(
            [Line(1, 500m), Line(2, -500m)],
            Environment(windowFrom: new DateOnly(2026, 8, 1), windowTo: new DateOnly(2026, 8, 31)));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Refuses_a_line_missing_a_company_wide_mandatory_dimension()
    {
        var result = Validator().Validate(
            [Line(1, 500m), Line(2, -500m)],
            Environment(mandatory: [RequiredDepartment]));

        var failure = Find(result, "PLAT.DIMENSION.VALUE_REQUIRED");
        failure.Detail.ShouldNotBeNull().ShouldContain("Department");
    }

    [Fact]
    public void Accepts_a_line_that_carries_the_mandatory_dimension()
    {
        var result = Validator().Validate(
            [
                Line(1, 500m, dimensions: WithDepartment()),
                Line(2, -500m, dimensions: WithDepartment()),
            ],
            Environment(mandatory: [RequiredDepartment]));

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void A_dimension_that_is_not_company_wide_is_demanded_only_by_the_accounts_that_want_it()
    {
        // A department is required on costs and meaningless on a bank balance. Expressing that
        // as a company-wide flag would force a value onto every bank transfer.
        var costAccount = Account(
            "5100",
            "Office expenses",
            requiredDimensions: new HashSet<Guid> { Department });

        var bankAccount = Account("1100", "Bank");

        var result = Validator().Validate(
            [Line(1, 500m, bankAccount), Line(2, -500m, bankAccount)],
            Environment(mandatory: [OptionalDepartment]));

        result.Succeeded.ShouldBeTrue();

        var onCost = Validator().Validate(
            [Line(1, 500m, costAccount), Line(2, -500m, bankAccount)],
            Environment(mandatory: [OptionalDepartment]));

        onCost.Failures.ShouldContain(m => m.Code.Value == "PLAT.DIMENSION.VALUE_REQUIRED");
    }

    [Fact]
    public void Reports_every_problem_in_one_pass()
    {
        // The difference between a five-minute correction and an afternoon: the user should not
        // have to fix one problem, post again, and discover the next.
        var result = Validator().Validate(
            [
                Line(1, 0m, Account("4000", "Revenue", GlAccountType.Total)),
                new PostingLineView(2, OpenDate, 500m, null),
                Line(3, 250m, Account("5199", "Old", blocked: true)),
            ],
            Environment());

        var codes = result.Failures.Select(static m => m.Code.Value).Distinct().ToList();

        codes.ShouldContain("FIN.JOURNAL.AMOUNT_ZERO");
        codes.ShouldContain("FIN.ACCOUNT.NOT_POSTABLE");
        codes.ShouldContain("FIN.JOURNAL.ACCOUNT_MISSING");
        codes.ShouldContain("FIN.ACCOUNT.BLOCKED");
        codes.ShouldContain("FIN.JOURNAL.OUT_OF_BALANCE");
    }

    [Fact]
    public void Points_each_message_at_the_line_that_caused_it()
    {
        var result = Validator().Validate(
            [Line(1, 500m), Line(2, 0m)],
            Environment());

        var zero = Find(result, "FIN.JOURNAL.AMOUNT_ZERO");
        zero.Target.Field.ShouldBe("Lines[2]");
    }

    [Fact]
    public void Checks_the_balancing_account_as_carefully_as_the_main_one()
    {
        var result = Validator().Validate(
            [Line(1, 500m, balancing: Account("4000", "Revenue", GlAccountType.Heading))],
            Environment());

        result.Failures.ShouldContain(m => m.Code.Value == "FIN.ACCOUNT.NOT_POSTABLE");
    }

    [Fact]
    public void Splits_a_signed_amount_into_debit_and_credit_consistently()
    {
        Ledger.GlEntry.Split(150m).ShouldBe((150m, 0m));
        Ledger.GlEntry.Split(-150m).ShouldBe((0m, 150m));
        Ledger.GlEntry.Split(0m).ShouldBe((0m, 0m));
    }
}
