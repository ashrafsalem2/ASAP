using ASAP.Modules.Pos.Receipts;
using ASAP.Platform.Kernel.Promotions;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos.Reporting;

/// <summary>
/// Answers, for the till, which offers actually took money off something.
/// </summary>
/// <remarks>
/// The cost comes from what the costing engine said at the moment of sale -- the same figure the
/// margin floor was checked against when the offer was allowed to apply. That is deliberate: a
/// report and a refusal that read different numbers would eventually disagree, and the report
/// would be the one nobody could argue with.
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class PosOfferUsage(AsapDbContext context) : IOfferUsage
{
    /// <inheritdoc />
    public string SourceCode => "POS";

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfferUsageLine>> BetweenAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // Method syntax rather than a query expression: the period this reads is bounded by
        // parameters called from and to, and "from" inside a query expression is the keyword
        // rather than the parameter.
        var lines = context.Set<PosReceiptLine>()
            .AsNoTracking()
            .Where(l => l.OfferCode != null)
            .Join(
                context.Set<PosReceipt>().AsNoTracking(),
                static l => l.PosReceiptId,
                static r => r.Id,
                static (l, r) => new { Line = l, Receipt = r })
            .Where(x => x.Receipt.BusinessDate >= from
                && x.Receipt.BusinessDate <= to
                && x.Receipt.Status == PosReceiptStatus.Posted)
            .Select(static x => new
            {
                x.Line.OfferCode,
                x.Receipt.BusinessDate,
                x.Receipt.No,
                x.Line.ItemNo,
                x.Line.Quantity,
                x.Line.OfferDiscountAmount,
                x.Line.UnitPrice,
                x.Line.DiscountPercent,
                x.Line.UnitCostAtSale,
            });

        var found = await lines.ToListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. found.Select(static l => new OfferUsageLine(
                l.OfferCode!,
                l.BusinessDate,
                l.No,
                l.ItemNo ?? string.Empty,
                l.Quantity,

                // Reported positive. Nobody reads an uptake report expecting the amount given
                // away to carry a minus sign.
                Math.Abs(l.OfferDiscountAmount),
                (l.Quantity * l.UnitPrice * (1m - (l.DiscountPercent / 100m))) - l.OfferDiscountAmount,
                l.UnitCostAtSale,
                "POS")),
        ];
    }
}
