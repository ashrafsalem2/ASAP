using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;

namespace ASAP.Modules.Finance.Posting;

/// <summary>
/// Decides whether a set of journal lines may be posted, and says exactly why when they may not.
/// </summary>
/// <remarks>
/// <para>
/// Pure logic with no database behind it, which is what makes every rule here provable by a test
/// rather than by inspection. Each rule exists because breaking it produces books that do not
/// reconcile, and the comments say which failure each one prevents.
/// </para>
/// <para>
/// Every rule runs, always. The user is told everything wrong with the batch in one pass instead
/// of discovering the problems one failed posting at a time, which is what makes the difference
/// between a five-minute correction and an afternoon.
/// </para>
/// </remarks>
/// <param name="messages">Renders the refusals.</param>
public sealed class JournalPostingValidator(IMessageCatalog messages)
{
    /// <summary>
    /// Checks a batch.
    /// </summary>
    /// <param name="lines">The lines about to be posted.</param>
    /// <param name="environment">The calendar, the posting window and what the caller may override.</param>
    /// <returns>
    /// A failed result carrying every problem found, or a successful one that may still carry
    /// warnings where the caller overrode a block.
    /// </returns>
    public Result Validate(IReadOnlyList<PostingLineView> lines, PostingEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(environment);

        var found = new List<AsapMessage>();

        if (lines.Count == 0)
        {
            found.Add(Raise(
                environment,
                FinanceMessages.BatchEmpty,
                new() { ["Batch"] = environment.BatchCode }));

            return Result.Failure(found);
        }

        foreach (var line in lines)
        {
            CheckLine(line, environment, found);
        }

        CheckBalance(lines, environment, found);

        return found.Exists(static m => m.IsFailure)
            ? Result.Failure(found)
            : Result.Success(found);
    }

    private void CheckLine(PostingLineView line, PostingEnvironment environment, List<AsapMessage> found)
    {
        var target = MessageTarget.OnField($"Lines[{line.LineNo}]");

        // A line that posts nothing is almost always a half-finished edit rather than an
        // intention. Letting it through writes a zero entry that clutters every ledger enquiry
        // the account will ever appear in.
        if (line.Amount == 0m)
        {
            found.Add(Raise(
                environment,
                FinanceMessages.AmountZero,
                new() { ["LineNo"] = line.LineNo },
                target));
        }

        CheckAccount(line, line.Account, environment, found, target);

        if (line.BalancingAccount is not null)
        {
            CheckAccount(line, line.BalancingAccount, environment, found, target);
        }

        CheckPeriod(line, environment, found, target);
        CheckDimensions(line, environment, found, target);
    }

    private void CheckAccount(
        PostingLineView line,
        PostingAccountView? account,
        PostingEnvironment environment,
        List<AsapMessage> found,
        MessageTarget target)
    {
        if (account is null)
        {
            found.Add(Raise(
                environment,
                FinanceMessages.AccountMissing,
                new() { ["LineNo"] = line.LineNo },
                target));

            return;
        }

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["LineNo"] = line.LineNo,
            ["AccountNo"] = account.No,
            ["AccountName"] = account.Name,
            ["AccountType"] = account.AccountType.ToString(),
        };

        // A heading or a totalling account is part of the report, not a place a balance can rest.
        // An entry on a totalling account is counted twice: once as itself and once inside the
        // range it sums, so the chart stops adding up.
        if (account.AccountType is not Accounts.GlAccountType.Posting)
        {
            found.Add(Raise(environment, FinanceMessages.AccountNotPostable, arguments, target));
            return;
        }

        if (account.IsBlocked)
        {
            found.Add(Raise(environment, FinanceMessages.AccountBlocked, arguments, target));
            return;
        }

        // Control accounts are written only by the module that owns them. A hand-keyed entry to
        // receivables makes the control account disagree with the customer ledger behind it, and
        // that difference is found months later by someone reconciling at year end.
        if (environment.IsManualEntry && !account.AllowsDirectPosting)
        {
            found.Add(Raise(environment, FinanceMessages.DirectPostingNotAllowed, arguments, target));
        }
    }

    private void CheckPeriod(
        PostingLineView line,
        PostingEnvironment environment,
        List<AsapMessage> found,
        MessageTarget target)
    {
        var status = environment.ResolvePeriod(line.PostingDate);

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["LineNo"] = line.LineNo,
            ["PostingDate"] = line.PostingDate,
            ["PeriodName"] = status.PeriodName,
            ["FiscalYear"] = status.FiscalYearCode,
        };

        switch (status.Availability)
        {
            case PeriodAvailability.NotDefined:
                found.Add(Raise(environment, FinanceMessages.NoOpenPeriod, arguments, target));
                break;

            case PeriodAvailability.PeriodClosed:
                found.Add(Raise(environment, FinanceMessages.PeriodClosed, arguments, target));
                break;

            case PeriodAvailability.YearClosed:
                // Deliberately not overridable, and the catalogue declares no override permission
                // for it. The statements for a closed year have been issued and possibly audited;
                // a late entry would make the filed figures wrong with nothing on the face of the
                // accounts to show it happened.
                found.Add(Raise(environment, FinanceMessages.YearClosed, arguments, target));
                break;

            case PeriodAvailability.Open:
            default:
                break;
        }

        CheckPostingWindow(line, environment, found, target);
    }

    /// <summary>
    /// Checks the date against the window this particular user may post in.
    /// </summary>
    /// <remarks>
    /// Separate from the period check, and narrower. Periods are closed for everyone at once,
    /// while a posting window is per user: a clerk is held to the current month while the
    /// financial controller keeps a wider one open to finish the close.
    /// </remarks>
    private void CheckPostingWindow(
        PostingLineView line,
        PostingEnvironment environment,
        List<AsapMessage> found,
        MessageTarget target)
    {
        var tooEarly = environment.PostingWindowFrom is { } from && line.PostingDate < from;
        var tooLate = environment.PostingWindowTo is { } to && line.PostingDate > to;

        if (!tooEarly && !tooLate)
        {
            return;
        }

        found.Add(Raise(
            environment,
            FinanceMessages.OutsidePostingWindow,
            new()
            {
                ["LineNo"] = line.LineNo,
                ["PostingDate"] = line.PostingDate,
                ["WindowFrom"] = environment.PostingWindowFrom,
                ["WindowTo"] = environment.PostingWindowTo,
            },
            target));
    }

    private void CheckDimensions(
        PostingLineView line,
        PostingEnvironment environment,
        List<AsapMessage> found,
        MessageTarget target)
    {
        // A dimension is demanded either by the company, of every entry, or by one of the accounts
        // on this line. The account-level demand is what expresses an analysis that only matters
        // in places: every cost account must name a department, while a bank balance need not.
        var demandedByAccount = line.Account?.RequiredDimensions;
        var demandedByBalancing = line.BalancingAccount?.RequiredDimensions;

        foreach (var dimension in environment.Mandatory)
        {
            var required = dimension.IsCompanyWide
                           || demandedByAccount?.Contains(dimension.Id) == true
                           || demandedByBalancing?.Contains(dimension.Id) == true;

            if (!required || line.Dimensions.ValueOf(dimension.Id) is not null)
            {
                continue;
            }

            found.Add(Raise(
                environment,
                ASAP.Platform.Core.Messaging.PlatformMessages.DimensionRequired,
                new()
                {
                    ["LineNo"] = line.LineNo,
                    ["Dimension"] = dimension.Name,
                },
                target));
        }
    }

    /// <summary>
    /// Checks the batch balances.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only lines without a balancing account count. A line that names one produces two entries
    /// that net to nothing, so it balances itself and must not be counted again.
    /// </para>
    /// <para>
    /// Amounts are rounded to the currency's decimals before summing, so the batch is judged on
    /// the figures that will actually be stored. Without that, three lines each a third of a
    /// fils out would pass a raw-decimal comparison and post a ledger that does not balance.
    /// </para>
    /// </remarks>
    private void CheckBalance(
        IReadOnlyList<PostingLineView> lines,
        PostingEnvironment environment,
        List<AsapMessage> found)
    {
        var debit = 0m;
        var credit = 0m;

        foreach (var line in lines)
        {
            if (line.BalancingAccount is not null)
            {
                continue;
            }

            var amount = Math.Round(line.Amount, environment.CurrencyDecimals, MidpointRounding.AwayFromZero);

            if (amount >= 0)
            {
                debit += amount;
            }
            else
            {
                credit += -amount;
            }
        }

        var difference = debit - credit;

        if (difference == 0m)
        {
            return;
        }

        found.Add(Raise(
            environment,
            FinanceMessages.OutOfBalance,
            new()
            {
                ["Debit"] = debit,
                ["Credit"] = credit,
                ["Difference"] = Math.Abs(difference),
                ["Currency"] = environment.CurrencyCode,
            }));
    }

    /// <summary>
    /// Renders a message, downgrading a block the caller is permitted to override.
    /// </summary>
    /// <remarks>
    /// This is where the override permission on a message definition earns its place. A caller
    /// holding <c>Finance.Period.Override</c> is not stopped by a closed period; they are warned,
    /// the posting proceeds, and the warning is written to the audit log as an override, so the
    /// fact that someone pushed past a protection is on the record.
    /// </remarks>
    private AsapMessage Raise(
        PostingEnvironment environment,
        MessageCode code,
        Dictionary<string, object?>? arguments = null,
        MessageTarget target = default)
    {
        var rendered = messages.Render(code, arguments, target);

        return environment.CanOverride(rendered.OverridePermission)
            ? messages.AsOverridden(rendered)
            : rendered;
    }
}
