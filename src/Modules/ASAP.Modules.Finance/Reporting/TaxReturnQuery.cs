using ASAP.Modules.Finance.Tax;
using ASAP.Platform.Kernel.Cqrs;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>One line of a tax return: everything at one rate, on one side.</summary>
/// <param name="TaxCodeNo">The code.</param>
/// <param name="Description">What it is.</param>
/// <param name="DescriptionArabic">What it is, in Arabic.</param>
/// <param name="Kind">How the code behaves, which decides the box it belongs in.</param>
/// <param name="Direction">Whether it is tax charged out or tax paid in.</param>
/// <param name="Percentage">The rate. Several rates under one code appear as several lines.</param>
/// <param name="BaseAmount">The taxable amount.</param>
/// <param name="TaxAmount">The tax on it.</param>
/// <param name="EntryCount">How many entries make it up, so a figure can be drilled into.</param>
public sealed record TaxReturnLine(
    string TaxCodeNo,
    string Description,
    string? DescriptionArabic,
    string Kind,
    string Direction,
    decimal Percentage,
    decimal BaseAmount,
    decimal TaxAmount,
    int EntryCount);

/// <summary>What a company owes the authority for a period, or is owed by it.</summary>
/// <param name="From">First day covered.</param>
/// <param name="To">Last day covered.</param>
/// <param name="CurrencyCode">Currency the figures are in.</param>
/// <param name="Lines">One line per code, rate and direction.</param>
/// <param name="OutputBase">Total taxable sales.</param>
/// <param name="OutputTax">Tax charged to customers.</param>
/// <param name="InputBase">Total taxable purchases.</param>
/// <param name="InputTax">Tax paid to vendors, reclaimable.</param>
/// <param name="NetPayable">
/// Output tax less input tax. Positive is owed to the authority, negative is a refund due.
/// </param>
/// <param name="ExemptBase">Exempt supplies, reported separately and carrying no tax.</param>
/// <param name="ZeroRatedBase">Zero-rated supplies, taxable at nothing and still declarable.</param>
/// <param name="EntriesAlreadyFiled">
/// How many entries in the range belong to a return already filed. Anything above zero means the
/// figures here are not what was declared.
/// </param>
public sealed record TaxReturn(
    DateOnly From,
    DateOnly To,
    string CurrencyCode,
    IReadOnlyList<TaxReturnLine> Lines,
    decimal OutputBase,
    decimal OutputTax,
    decimal InputBase,
    decimal InputTax,
    decimal NetPayable,
    decimal ExemptBase,
    decimal ZeroRatedBase,
    int EntriesAlreadyFiled);

/// <summary>Asks what is owed to the tax authority for a period.</summary>
/// <param name="From">First day to include.</param>
/// <param name="To">Last day to include.</param>
/// <param name="IncludeFiled">
/// Whether to include entries already declared in a filed return. Off by default, so the figure
/// is what still needs declaring rather than what the period once came to.
/// </param>
[RequiresPermission("Finance", "Report", PermissionAction.Read)]
public sealed record TaxReturnQuery(
    DateOnly From,
    DateOnly To,
    bool IncludeFiled = false) : IQuery<TaxReturn>;

/// <summary>
/// Builds the tax return.
/// </summary>
/// <remarks>
/// <para>
/// Built from the tax entries rather than from the tax accounts, which is the whole reason those
/// entries exist. The balance on the VAT account is one net number: it cannot say which supplies
/// were standard-rated, which were zero-rated and which were exempt, and each of those is its own
/// box on the form. Zero-rated sales make the point -- they move the tax account by nothing at
/// all, and still have to be declared.
/// </para>
/// <para>
/// Grouped by rate as well as by code, so a period spanning a rate change reports the two
/// separately instead of averaging them into a percentage that was never charged.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class TaxReturnQueryHandler(AsapDbContext context)
    : IRequestHandler<TaxReturnQuery, TaxReturn>
{
    /// <inheritdoc />
    public async Task<TaxReturn> HandleAsync(
        TaxReturnQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = context.Set<TaxEntry>()
            .AsNoTracking()
            .Where(e => e.PostingDate >= request.From && e.PostingDate <= request.To);

        var alreadyFiled = await query
            .CountAsync(static e => e.IsClosed, cancellationToken)
            .ConfigureAwait(false);

        if (!request.IncludeFiled)
        {
            query = query.Where(static e => !e.IsClosed);
        }

        var grouped = await query
            .GroupBy(static e => new { e.TaxCodeNo, e.Kind, e.Direction, e.Percentage })
            .Select(static g => new
            {
                g.Key.TaxCodeNo,
                g.Key.Kind,
                g.Key.Direction,
                g.Key.Percentage,
                BaseAmount = g.Sum(static e => e.BaseAmount),
                TaxAmount = g.Sum(static e => e.TaxAmount),
                EntryCount = g.Count(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var descriptions = await context.Set<TaxCode>()
            .AsNoTracking()
            .Select(static c => new { c.Code, c.Description, c.DescriptionArabic })
            .ToDictionaryAsync(static c => c.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var lines = grouped
            .OrderBy(static g => g.Direction)
            .ThenBy(static g => g.TaxCodeNo)
            .ThenBy(static g => g.Percentage)
            .Select(g => new TaxReturnLine(
                g.TaxCodeNo,
                descriptions.GetValueOrDefault(g.TaxCodeNo)?.Description ?? g.TaxCodeNo,
                descriptions.GetValueOrDefault(g.TaxCodeNo)?.DescriptionArabic,
                g.Kind.ToString(),
                g.Direction.ToString(),
                g.Percentage,
                g.BaseAmount,
                g.TaxAmount,
                g.EntryCount))
            .ToList();

        decimal Sum(TaxDirection direction, Func<TaxReturnLine, decimal> field)
            => lines.Where(l => l.Direction == direction.ToString()).Sum(field);

        var outputTax = Sum(TaxDirection.Output, static l => l.TaxAmount);
        var inputTax = Sum(TaxDirection.Input, static l => l.TaxAmount);

        var currency = await context.Companies
                           .AsNoTracking()
                           .Select(static c => c.BaseCurrencyCode)
                           .FirstOrDefaultAsync(cancellationToken)
                           .ConfigureAwait(false)
                       ?? "SAR";

        return new TaxReturn(
            request.From,
            request.To,
            currency,
            lines,
            Sum(TaxDirection.Output, static l => l.BaseAmount),
            outputTax,
            Sum(TaxDirection.Input, static l => l.BaseAmount),
            inputTax,

            // What actually changes hands. Positive is owed to the authority; negative is a
            // refund, which is ordinary for an exporter and worth showing as such rather than as
            // a negative payment.
            outputTax - inputTax,
            BaseOf(lines, TaxKind.Exempt),
            BaseOf(lines, TaxKind.ZeroRated),
            alreadyFiled);
    }

    private static decimal BaseOf(IEnumerable<TaxReturnLine> lines, TaxKind kind)
        => lines.Where(l => l.Kind == kind.ToString()).Sum(static l => l.BaseAmount);
}
