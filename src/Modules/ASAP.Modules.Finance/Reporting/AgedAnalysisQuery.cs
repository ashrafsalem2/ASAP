using ASAP.Modules.Finance.Parties;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>What one party owes, split by how late it is.</summary>
/// <param name="PartyNo">The party number.</param>
/// <param name="Name">The party name.</param>
/// <param name="NameArabic">The Arabic name.</param>
/// <param name="Buckets">
/// What is outstanding in each band, in the same order as the report's band labels.
/// </param>
/// <param name="Total">Everything outstanding, whatever its age.</param>
/// <param name="OldestDocumentNo">The oldest thing still unpaid, which is what gets chased.</param>
/// <param name="OldestDaysOverdue">How late that oldest document is.</param>
/// <param name="CreditLimit">The party's limit, or zero when they have none.</param>
/// <param name="IsOverLimit">Whether what they owe now exceeds that limit.</param>
public sealed record AgedAnalysisRow(
    string PartyNo,
    string Name,
    string? NameArabic,
    IReadOnlyList<decimal> Buckets,
    decimal Total,
    string? OldestDocumentNo,
    int OldestDaysOverdue,
    decimal CreditLimit,
    bool IsOverLimit);

/// <summary>What is owed, and how late it is.</summary>
/// <param name="AsAt">The date everything was aged against.</param>
/// <param name="Kind">Which ledger was aged.</param>
/// <param name="CurrencyCode">Currency the figures are in.</param>
/// <param name="BandLabels">The bands, in order, for example <c>Not due</c> then <c>1-30</c>.</param>
/// <param name="Rows">One row per party with something outstanding.</param>
/// <param name="BucketTotals">Column totals, in the same order as the bands.</param>
/// <param name="Total">Everything outstanding across every party.</param>
public sealed record AgedAnalysis(
    DateOnly AsAt,
    string Kind,
    string CurrencyCode,
    IReadOnlyList<string> BandLabels,
    IReadOnlyList<AgedAnalysisRow> Rows,
    IReadOnlyList<decimal> BucketTotals,
    decimal Total);

/// <summary>
/// Asks what is owed on a given day and how late each part of it is.
/// </summary>
/// <param name="Kind">Which ledger to age: customers or vendors.</param>
/// <param name="AsAt">The day to age against. Defaults to today at the endpoint.</param>
/// <param name="BandDays">
/// Where the bands break, in days overdue. Thirty-day bands are the convention, but a business
/// selling on seven-day terms wants seven-day bands, and hard-coding thirty makes the report
/// useless to them.
/// </param>
[RequiresPermission("Finance", "Report", PermissionAction.Read)]
public sealed record AgedAnalysisQuery(
    PartyKind Kind,
    DateOnly AsAt,
    IReadOnlyList<int>? BandDays = null) : IQuery<AgedAnalysis>;

/// <summary>
/// Builds the aged analysis.
/// </summary>
/// <remarks>
/// <para>
/// Ages by due date rather than posting date. An invoice raised in January on ninety-day terms is
/// not overdue in February, and a report that says it is trains people to ignore the report.
/// </para>
/// <para>
/// Reads only open entries, which is what the filtered index on the ledger exists for. A settled
/// invoice has nothing outstanding by definition, and a ledger is mostly settled entries within a
/// year of going live.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="clock">Supplies today when none is given.</param>
public sealed class AgedAnalysisQueryHandler(AsapDbContext context, IClock clock)
    : IRequestHandler<AgedAnalysisQuery, AgedAnalysis>
{
    /// <summary>The bands used when the caller names none.</summary>
    private static readonly int[] DefaultBands = [30, 60, 90];

    /// <inheritdoc />
    public async Task<AgedAnalysis> HandleAsync(
        AgedAnalysisQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var asAt = request.AsAt == default ? clock.Today : request.AsAt;
        var bands = Bands(request.BandDays);

        var open = request.Kind is PartyKind.Customer
            ? await OpenAsync<CustomerLedgerEntry>(asAt, cancellationToken).ConfigureAwait(false)
            : await OpenAsync<VendorLedgerEntry>(asAt, cancellationToken).ConfigureAwait(false);

        var limits = request.Kind is PartyKind.Customer
            ? await LimitsAsync<Customer>(cancellationToken).ConfigureAwait(false)
            : await LimitsAsync<Vendor>(cancellationToken).ConfigureAwait(false);

        var rows = new List<AgedAnalysisRow>();

        // One column for what is not yet due, one per band, and one for everything past the last
        // band -- which is the column anybody reading this report looks at first.
        var columns = bands.Count + 2;
        var totals = new decimal[columns];

        foreach (var group in open.GroupBy(static e => e.PartyNo).OrderBy(static g => g.Key))
        {
            var buckets = new decimal[columns];
            var total = 0m;
            var oldest = group.MinBy(static e => e.DueDate);

            foreach (var entry in group)
            {
                var bucket = BucketFor(entry.DaysOverdue, bands);

                buckets[bucket] += entry.RemainingAmount;
                totals[bucket] += entry.RemainingAmount;
                total += entry.RemainingAmount;
            }

            // A party whose debits and credits happen to cancel has nothing to chase, and a row of
            // zeroes on a chase list is a row somebody has to read and dismiss.
            if (total == 0m && buckets.All(static b => b == 0m))
            {
                continue;
            }

            var party = limits.GetValueOrDefault(group.Key);

            rows.Add(new AgedAnalysisRow(
                group.Key,
                group.First().PartyName,
                party.NameArabic,
                buckets,
                total,
                oldest.DocumentNo,
                oldest.DaysOverdue,
                party.CreditLimit,
                party.CreditLimit > 0m && total > party.CreditLimit));
        }

        var currency = await context.Companies
                           .AsNoTracking()
                           .Select(static c => c.BaseCurrencyCode)
                           .FirstOrDefaultAsync(cancellationToken)
                           .ConfigureAwait(false)
                       ?? "SAR";

        return new AgedAnalysis(
            asAt,
            request.Kind.ToString(),
            currency,
            Labels(bands),
            rows,
            totals,
            totals.Sum());
    }

    /// <summary>One open entry, flattened to what the ageing needs.</summary>
    private readonly record struct OpenEntry(
        string PartyNo,
        string PartyName,
        string? DocumentNo,
        DateOnly DueDate,
        decimal RemainingAmount,
        int DaysOverdue);

    private async Task<List<OpenEntry>> OpenAsync<TEntry>(
        DateOnly asAt,
        CancellationToken cancellationToken)
        where TEntry : PartyLedgerEntry
    {
        var entries = await context.Set<TEntry>()
            .AsNoTracking()
            .Where(e => e.IsOpen && e.PostingDate <= asAt)
            .Select(static e => new
            {
                e.PartyNo,
                e.PartyName,
                e.DocumentNo,
                e.DueDate,
                e.RemainingAmount,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. entries.Select(e => new OpenEntry(
                e.PartyNo,
                e.PartyName,
                e.DocumentNo,
                e.DueDate,
                e.RemainingAmount,
                asAt <= e.DueDate ? 0 : asAt.DayNumber - e.DueDate.DayNumber)),
        ];
    }

    private async Task<Dictionary<string, (decimal CreditLimit, string? NameArabic)>> LimitsAsync<TParty>(
        CancellationToken cancellationToken)
        where TParty : Party
        => await context.Set<TParty>()
            .AsNoTracking()
            .Select(static p => new { p.No, p.CreditLimit, p.NameArabic })
            .ToDictionaryAsync(
                static p => p.No,
                static p => (p.CreditLimit, p.NameArabic),
                StringComparer.OrdinalIgnoreCase,
                cancellationToken)
            .ConfigureAwait(false);

    private static List<int> Bands(IReadOnlyList<int>? requested)
    {
        var bands = (requested is { Count: > 0 } ? requested : DefaultBands)
            .Where(static d => d > 0)
            .Distinct()
            .Order()
            .ToList();

        return bands.Count > 0 ? bands : [.. DefaultBands];
    }

    /// <summary>
    /// Which column a number of days falls in. Column zero is everything not yet due, and the
    /// last column is everything past the final band.
    /// </summary>
    private static int BucketFor(int daysOverdue, List<int> bands)
    {
        if (daysOverdue <= 0)
        {
            return 0;
        }

        for (var index = 0; index < bands.Count; index++)
        {
            if (daysOverdue <= bands[index])
            {
                return index + 1;
            }
        }

        return bands.Count + 1;
    }

    /// <summary>
    /// Names the columns, in the order the buckets are filled.
    /// </summary>
    /// <remarks>
    /// Returned as codes rather than sentences. The client translates <c>NotDue</c> and
    /// <c>Over90</c> into its own language; a server that shipped "Not due" would leave the Arabic
    /// report with an English column heading.
    /// </remarks>
    private static List<string> Labels(List<int> bands)
    {
        var labels = new List<string>(bands.Count + 2) { "NotDue" };
        var previous = 0;

        foreach (var band in bands)
        {
            labels.Add($"{previous + 1}-{band}");
            previous = band;
        }

        labels.Add($"Over{bands[^1]}");

        return labels;
    }
}
