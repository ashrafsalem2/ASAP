using ASAP.Modules.Finance.Posting;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Periods;

/// <summary>
/// Answers what the fiscal calendar says about a date.
/// </summary>
/// <remarks>
/// <para>
/// Loaded once per posting rather than queried per line. A journal of two hundred lines asks about
/// at most a handful of distinct dates, and the whole calendar for a company is a few dozen rows,
/// so reading it once and answering from memory is both faster and simpler than caching per date.
/// </para>
/// <para>
/// A year being closed beats a period being open. Closing a year does not necessarily close every
/// period inside it, and if the order were reversed a company that closed its year without closing
/// December would still accept December postings.
/// </para>
/// </remarks>
public sealed class FiscalCalendar
{
    private readonly List<(FiscalYear Year, List<FiscalPeriod> Periods)> _years;

    private FiscalCalendar(List<(FiscalYear, List<FiscalPeriod>)> years)
    {
        _years = years;
    }

    /// <summary>Reads the calendar for the active company.</summary>
    /// <param name="context">The unit of work.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    public static async Task<FiscalCalendar> LoadAsync(
        AsapDbContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var years = await context.Set<FiscalYear>()
            .AsNoTracking()
            .Include(y => y.Periods)
            .OrderBy(y => y.StartDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new FiscalCalendar(
            [.. years.Select(y => (y, y.Periods.OrderBy(static p => p.PeriodNo).ToList()))]);
    }

    /// <summary>Whether a date may be posted to, and why not when it may not.</summary>
    /// <param name="date">The posting date.</param>
    public PeriodStatus Resolve(DateOnly date)
    {
        foreach (var (year, periods) in _years)
        {
            if (!year.Contains(date))
            {
                continue;
            }

            // Checked before the period, deliberately. Closing a year does not necessarily close
            // every period inside it, and the other order would let a company that closed its
            // year without closing December keep posting to December.
            if (year.IsClosed)
            {
                return new PeriodStatus(PeriodAvailability.YearClosed, FiscalYearCode: year.Code);
            }

            // An adjustment period shares its dates with the last trading period, so an ordinary
            // posting must land on the trading one. Adjustments are posted deliberately, by
            // naming the period, not by falling into it because the dates happened to match.
            var period = periods.Find(p => !p.IsAdjustment && p.Contains(date))
                         ?? periods.Find(p => p.Contains(date));

            if (period is null)
            {
                return new PeriodStatus(PeriodAvailability.NotDefined, FiscalYearCode: year.Code);
            }

            return period.IsClosed
                ? new PeriodStatus(PeriodAvailability.PeriodClosed, period.Name, year.Code)
                : PeriodStatus.Open(period.Name, year.Code);
        }

        return new PeriodStatus(PeriodAvailability.NotDefined);
    }

    /// <summary>The years the calendar covers, for reporting and for the period screen.</summary>
    public IReadOnlyList<FiscalYear> Years => [.. _years.Select(static y => y.Year)];
}
