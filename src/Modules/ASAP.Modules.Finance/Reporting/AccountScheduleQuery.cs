using ASAP.Modules.Finance.Accounts;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>One row of a run schedule.</summary>
/// <param name="RowNo">What formulas call it.</param>
/// <param name="Description">What it is called.</param>
/// <param name="DescriptionArabic">What it is called in Arabic.</param>
/// <param name="Amount">The figure, or null when it has no answer.</param>
/// <param name="Indent">How far to indent it.</param>
/// <param name="IsBold">Whether it is a total.</param>
/// <param name="IsHeading">Whether it carries no figure at all.</param>
public readonly record struct ScheduleRow(
    string RowNo,
    string Description,
    string? DescriptionArabic,
    decimal? Amount,
    int Indent,
    bool IsBold,
    bool IsHeading);

/// <summary>A statement, run.</summary>
/// <param name="Code">The schedule.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="From">First day included.</param>
/// <param name="To">Last day included.</param>
/// <param name="CurrencyCode">What the figures are in.</param>
/// <param name="Rows">The rows, in the order they are printed.</param>
public sealed record AccountScheduleReport(
    string Code,
    string Name,
    string? NameArabic,
    DateOnly From,
    DateOnly To,
    string CurrencyCode,
    IReadOnlyList<ScheduleRow> Rows);

/// <summary>
/// Runs a statement somebody defined.
/// </summary>
/// <param name="Code">The schedule to run.</param>
/// <param name="From">First day to include, or null for the start of the year containing To.</param>
/// <param name="To">Last day to include, or null for today.</param>
/// <param name="BranchId">One branch, or null for the whole company.</param>
[RequiresPermission("Finance", "Report", PermissionAction.Read)]
public sealed record AccountScheduleQuery(
    string Code,
    DateOnly? From = null,
    DateOnly? To = null,
    Guid? BranchId = null) : IQuery<Result<AccountScheduleReport>>;

/// <summary>
/// Turns a schedule's rows into figures.
/// </summary>
/// <remarks>
/// <para>
/// Rows that name accounts are worked out first, because they depend on nothing. Rows that name
/// other rows are worked out in waves: every pass resolves whatever has all its inputs ready. A
/// pass that resolves nothing while formulas remain means those formulas depend on each other in
/// a circle, and the circle is named rather than left to run out of stack.
/// </para>
/// <para>
/// That ordering also means the rows may be written in any order on the page. A statement that
/// shows its total at the top — which plenty do — works exactly as well as one that shows it at
/// the bottom.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="balances">Reads the ledger.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="clock">Supplies today.</param>
public sealed class AccountScheduleQueryHandler(
    AsapDbContext context,
    LedgerBalances balances,
    IMessageCatalog messages,
    IClock clock)
    : IRequestHandler<AccountScheduleQuery, Result<AccountScheduleReport>>
{
    /// <inheritdoc />
    public async Task<Result<AccountScheduleReport>> HandleAsync(
        AccountScheduleQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var code = request.Code.Trim().ToUpperInvariant();

        var schedule = await context.Set<AccountSchedule>()
            .AsNoTracking()
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (schedule is null)
        {
            return Result<AccountScheduleReport>.Failure(messages.Render(
                FinanceMessages.ScheduleNotFound,
                Args(("Schedule", code))));
        }

        var to = request.To ?? clock.Today;
        var from = request.From ?? await YearStartAsync(to, cancellationToken).ConfigureAwait(false);

        var movement = await MovementAsync(from, to, request.BranchId, cancellationToken).ConfigureAwait(false);
        var cumulative = await MovementAsync(null, to, request.BranchId, cancellationToken).ConfigureAwait(false);

        var lines = schedule.Lines.OrderBy(static l => l.Order).ToList();
        var amounts = new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase);
        var pending = new List<AccountScheduleLine>();

        foreach (var line in lines)
        {
            switch (line.Kind)
            {
                case ScheduleRowKind.Heading:
                    amounts[line.RowNo] = null;
                    break;

                case ScheduleRowKind.Accounts:
                    var source = line.AmountKind is ScheduleAmountKind.BalanceAtDate ? cumulative : movement;
                    var raw = AccountRange.Parse(line.Expression).Sum(source);

                    // Turned here, before any formula sees it, so a formula means what it looks
                    // like it means. Somebody writing "R10 - R20" is reading revenue and cost off
                    // the page in front of them; if formulas ran on the ledger's own signs, that
                    // subtraction would add, and the author would have no way of telling from
                    // what they can see.
                    amounts[line.RowNo] = line.ShowOppositeSign ? -raw : raw;
                    break;

                default:
                    pending.Add(line);
                    break;
            }
        }

        // Waves, not recursion. Each pass takes whatever now has all its inputs; when a pass takes
        // nothing and work remains, what remains refers to itself, directly or round a loop.
        while (pending.Count > 0)
        {
            var resolved = pending
                .Where(l => ScheduleFormula.ReferencesIn(l.Expression).All(amounts.ContainsKey))
                .ToList();

            if (resolved.Count == 0)
            {
                return Result<AccountScheduleReport>.Failure(messages.Render(
                    FinanceMessages.ScheduleFormulaCircular,
                    Args(
                        ("Schedule", schedule.Code),
                        ("Rows", string.Join(", ", pending.Select(static l => l.RowNo))))));
            }

            foreach (var line in resolved)
            {
                amounts[line.RowNo] = ScheduleFormula.Evaluate(line.Expression, amounts);
                pending.Remove(line);
            }
        }

        var rows = new List<ScheduleRow>();

        foreach (var line in lines)
        {
            var amount = amounts.GetValueOrDefault(line.RowNo);

            if (line.HideIfZero && line.Kind is not ScheduleRowKind.Heading && amount is null or 0m)
            {
                continue;
            }

            rows.Add(new ScheduleRow(
                line.RowNo,
                line.Description,
                line.DescriptionArabic,
                amount,
                line.Indent,
                line.IsBold,
                line.Kind is ScheduleRowKind.Heading));
        }

        return Result<AccountScheduleReport>.Success(new AccountScheduleReport(
            schedule.Code,
            schedule.Name,
            schedule.NameArabic,
            from,
            to,
            await balances.CurrencyAsync(cancellationToken).ConfigureAwait(false),
            rows));
    }

    /// <summary>
    /// Sums what each account moved, optionally at one branch.
    /// </summary>
    /// <remarks>
    /// The branch filter is here rather than in <see cref="LedgerBalances"/> because it is a
    /// question only this report asks. A trial balance filtered by branch would not add up: the
    /// entries a document posts centrally have no branch, and leaving them out breaks the one
    /// property a trial balance exists to have.
    /// </remarks>
    private async Task<Dictionary<string, decimal>> MovementAsync(
        DateOnly? from,
        DateOnly to,
        Guid? branchId,
        CancellationToken cancellationToken)
    {
        if (branchId is null)
        {
            return await balances.MovementAsync(from, to, cancellationToken).ConfigureAwait(false);
        }

        var entries = context.Set<Ledger.GlEntry>()
            .AsNoTracking()
            .Where(e => e.PostingDate <= to && e.BranchId == branchId);

        if (from is { } start)
        {
            entries = entries.Where(e => e.PostingDate >= start);
        }

        return await entries
            .GroupBy(static e => e.AccountNo)
            .Select(static g => new { AccountNo = g.Key, Amount = g.Sum(static e => e.Amount) })
            .ToDictionaryAsync(
                static x => x.AccountNo,
                static x => x.Amount,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The first day of the financial year the closing date falls in.
    /// </summary>
    /// <remarks>
    /// The year rather than the calendar year. A company whose year starts in April and asks for
    /// a profit and loss to September wants six months, not nine.
    /// </remarks>
    private async Task<DateOnly> YearStartAsync(DateOnly to, CancellationToken cancellationToken)
    {
        var year = await balances.YearContainingAsync(to, cancellationToken).ConfigureAwait(false);

        return year?.StartDate ?? new DateOnly(to.Year, 1, 1);
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
