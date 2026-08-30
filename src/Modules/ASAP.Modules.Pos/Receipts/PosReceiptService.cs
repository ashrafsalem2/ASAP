using ASAP.Modules.Finance.Journals;
using ASAP.Modules.Finance.Ledger;
using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Inventory.Items;
using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Posting;
using ASAP.Modules.Pos.Sessions;
using ASAP.Modules.Pos.Stations;
using ASAP.Modules.Promotions.Offers;
using ASAP.Modules.Promotions.Pricing;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Pos.Receipts;

/// <summary>One thing being rung up.</summary>
/// <param name="Type">Whether it sells stock or a charge.</param>
/// <param name="No">The item number, or the account number on a charge line.</param>
/// <param name="Quantity">How much. Negative takes goods back.</param>
/// <param name="UnitPrice">The price, or zero to take the item's own.</param>
/// <param name="DiscountPercent">A discount off this line.</param>
/// <param name="Description">What it says on the receipt. Falls back to the item name.</param>
/// <param name="TaxCode">The tax to charge. Falls back to the item's own.</param>
/// <param name="UnitCode">
/// The unit the quantity was rung in, or null for the item's base unit.
/// </param>
/// <param name="VariantCode">
/// Which colour, size or flavour, on an item that has them. A scan usually supplies it: a variant
/// carries its own barcode precisely so the label on the garment says which size.
/// </param>
public readonly record struct PosLineRequest(
    PosLineType Type,
    string No,
    decimal Quantity,
    decimal UnitPrice = 0m,
    decimal DiscountPercent = 0m,
    string? Description = null,
    string? TaxCode = null,
    string? UnitCode = null,
    string? VariantCode = null);

/// <summary>Money put towards a receipt.</summary>
/// <param name="Kind">What kind of money it is.</param>
/// <param name="Amount">How much was handed over, change included.</param>
/// <param name="Reference">The card's last four, the voucher number, whatever identifies it.</param>
public readonly record struct PosTenderRequest(
    TenderKind Kind,
    decimal Amount,
    string? Reference = null);

/// <summary>What a receipt posted.</summary>
/// <param name="ReceiptNo">The receipt number issued.</param>
/// <param name="TransactionNo">The transaction the entries posted under.</param>
/// <param name="NetAmount">The goods, after discount and before tax.</param>
/// <param name="DiscountAmount">What was given away.</param>
/// <param name="TaxAmount">Tax charged.</param>
/// <param name="RoundingAmount">What was rounded off to make the total payable.</param>
/// <param name="TotalAmount">What the customer paid.</param>
/// <param name="ChangeGiven">What was handed back.</param>
/// <param name="CostAmount">What the goods cost, charged to cost of sales.</param>
public readonly record struct PosReceiptPosted(
    string ReceiptNo,
    long TransactionNo,
    decimal NetAmount,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal RoundingAmount,
    decimal TotalAmount,
    decimal ChangeGiven,
    decimal CostAmount);

/// <summary>
/// Rings up a sale at a till and takes the money for it.
/// </summary>
/// <remarks>
/// <para>
/// One transaction does everything a sale at a counter involves: stock leaves at what it cost,
/// revenue is credited at list with the discount as a contra, tax lands on what the customer
/// actually pays, and the money goes wherever that kind of money goes. There is no order, no
/// shipment and no invoice, because at a till those three happen in the same second and pretending
/// otherwise would mean three documents for a bottle of water.
/// </para>
/// <para>
/// What it does not do is invent a different way of accounting for a sale. The revenue and tax
/// entries are the same shape a sales invoice writes, deliberately: a company that reports its
/// shop takings differently from its trade sales cannot add them up.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="overrides">Records every protection a receipt pushed past.</param>
/// <param name="offers">Supplies what is running today, and the margin floor.</param>
/// <param name="promotions">Decides which offers apply and what they take off.</param>
/// <param name="stock">Moves the goods.</param>
/// <param name="documents">Posts the money.</param>
/// <param name="branches">Says which branch a till stands in.</param>
/// <param name="numbers">Issues the receipt number.</param>
/// <param name="setup">Supplies the accounts, the rounding and the discount limit.</param>
/// <param name="clock">Supplies the time and the business date.</param>
/// <param name="logger">Records receipts posted.</param>
public sealed class PosReceiptService(
    AsapDbContext context,
    IMessageCatalog messages,
    OverrideAuditor overrides,
    OfferService offers,
    PromotionEngine promotions,
    StockPostingService stock,
    DocumentPostingService documents,
    Stations.StationBranchLookup branches,
    INumberSeriesService numbers,
    ISetupService setup,
    IClock clock,
    ILogger<PosReceiptService> logger)
{
    /// <summary>
    /// Rings a sale up, takes the money and posts everything.
    /// </summary>
    /// <param name="sessionNo">The open session it belongs to.</param>
    /// <param name="lines">What is being sold.</param>
    /// <param name="tenders">How it is being paid for.</param>
    /// <param name="customerNo">Who to record it against, or null for the till's walk-in customer.</param>
    /// <param name="returnsReceiptNo">The receipt being returned against, when there is one.</param>
    /// <param name="parkedReceiptNo">The parked sale this was recalled from, when it was.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="overrideReason">Why a protection is being pushed past.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What posted, or every reason it did not.</returns>
    public async Task<Result<PosReceiptPosted>> PostAsync(
        string sessionNo,
        IReadOnlyList<PosLineRequest> lines,
        IReadOnlyList<PosTenderRequest> tenders,
        string? customerNo = null,
        string? returnsReceiptNo = null,
        string? parkedReceiptNo = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        string? overrideReason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(tenders);

        var found = new List<AsapMessage>();

        var session = await context.Set<PosSession>()
            .FirstOrDefaultAsync(s => s.No == sessionNo, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return Result<PosReceiptPosted>.Failure(
                messages.Render(
                    PosMessages.SessionNotFound,
                    Args(("SessionNo", sessionNo))));
        }

        if (!session.IsOpen)
        {
            return Result<PosReceiptPosted>.Failure(
                messages.Render(
                    PosMessages.SessionClosed,
                    Args(("SessionNo", session.No), ("ClosedAt", session.ClosedAtUtc))));
        }

        var station = await context.Set<PosStation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == session.StationCode, cancellationToken)
            .ConfigureAwait(false);

        if (station is null)
        {
            return Result<PosReceiptPosted>.Failure(
                messages.Render(
                    PosMessages.StationNotFound,
                    Args(("StationCode", session.StationCode))));
        }

        if (lines.Count == 0)
        {
            return Result<PosReceiptPosted>.Failure(
                messages.Render(PosMessages.ReceiptHasNoLines, Args(("SessionNo", session.No))));
        }

        var items = await ResolveItemsAsync(lines, cancellationToken).ConfigureAwait(false);
        var discountLimit = await DiscountLimitAsync(cancellationToken).ConfigureAwait(false);

        // What the goods went out at, when this is a return against a receipt we can read. A
        // customer bringing back something they bought on offer is owed what they paid, not what
        // it happens to cost today, and only the original document knows which.
        var original = await OriginalAsync(returnsReceiptNo, found, cancellationToken)
            .ConfigureAwait(false);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PosReceiptPosted>.Failure(found);
        }

        var units = await ResolveUnitsAsync(lines, items, cancellationToken).ConfigureAwait(false);

        var built = BuildLines(lines, items, units, discountLimit, original, heldOverridePermissions, found);

        built = await ApplyOffersAsync(
                built,
                items,
                station,
                session,
                heldOverridePermissions,
                found,
                cancellationToken)
            .ConfigureAwait(false);

        if (original is not null)
        {
            await CheckReturnAsync(
                    original,
                    built,
                    heldOverridePermissions,
                    found,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PosReceiptPosted>.Failure(found);
        }

        var settled = await SettleAsync(
                station,
                customerNo,
                built,
                tenders,
                found,
                cancellationToken)
            .ConfigureAwait(false);

        if (settled is null || found.Exists(static m => m.IsFailure))
        {
            return Result<PosReceiptPosted>.Failure(found);
        }

        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<PosReceiptPosted>.FailureFrom(numbered);
        }

        var receipt = NewReceipt(
            session,
            station,
            settled,
            built,
            numbered.Value,
            today,
            clock.UtcNow,
            returnsReceiptNo);

        // Stock first. If the goods cannot move there is no sale to account for, and a till that
        // took the money and then discovered that would have to give it back.
        var cost = await MoveStockAsync(
                receipt,
                station,
                built,
                heldOverridePermissions,
                overrideReason,
                found,
                cancellationToken)
            .ConfigureAwait(false);

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PosReceiptPosted>.Failure(found);
        }

        receipt.CostAmount = cost;

        var posted = await PostMoneyAsync(receipt, built, settled, overrideReason, cancellationToken)
            .ConfigureAwait(false);

        if (posted.Failed)
        {
            return Result<PosReceiptPosted>.FailureFrom(posted);
        }

        found.AddRange(posted.Messages.Where(static m => m.Severity is not MessageSeverity.Success));

        receipt.TransactionNo = posted.Value.TransactionNo;
        receipt.Status = PosReceiptStatus.Posted;

        context.Set<PosReceipt>().Add(receipt);

        if (parkedReceiptNo is not null)
        {
            // Recalled and paid for. The basket is voided rather than deleted, so the trail still
            // shows that something was set aside and what became of it.
            var parked = await context.Set<PosReceipt>()
                .FirstOrDefaultAsync(r => r.No == parkedReceiptNo, cancellationToken)
                .ConfigureAwait(false);

            if (parked is { Status: PosReceiptStatus.Parked })
            {
                parked.Status = PosReceiptStatus.Voided;
                parked.ParkedAs = null;
            }
        }

        Accumulate(session, receipt, settled);

        overrides.Record(found, "Pos.Receipt", receipt.No, overrideReason);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Posted receipt {ReceiptNo} at {StationCode} for {TotalAmount}, {LineCount} line(s).",
            receipt.No,
            receipt.StationCode,
            receipt.TotalAmount,
            receipt.Lines.Count);

        return Result<PosReceiptPosted>.Success(
            new PosReceiptPosted(
                receipt.No,
                posted.Value.TransactionNo,
                receipt.NetAmount,
                receipt.DiscountAmount,
                receipt.TaxAmount,
                receipt.RoundingAmount,
                receipt.TotalAmount,
                receipt.ChangeGiven,
                receipt.CostAmount),
            found);
    }

    /// <summary>
    /// Sets a sale aside so the till can serve somebody else.
    /// </summary>
    /// <remarks>
    /// Nothing posts and nothing is reserved. A parked sale is a basket, not a document, so it
    /// does not take a receipt number: a tax invoice sequence with numbers issued to baskets that
    /// were never paid for is a sequence somebody has to explain. It takes a handle built from
    /// the session instead, which is obviously not an invoice number and cannot collide.
    /// </remarks>
    /// <param name="sessionNo">The open session it belongs to.</param>
    /// <param name="lines">What has been scanned so far.</param>
    /// <param name="parkedAs">What to call it when recalling, such as the customer's name.</param>
    /// <param name="customerNo">Who it is for, or null for the till's walk-in customer.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The parked sale, or every reason it could not be set aside.</returns>
    public async Task<Result<PosReceipt>> ParkAsync(
        string sessionNo,
        IReadOnlyList<PosLineRequest> lines,
        string? parkedAs = null,
        string? customerNo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var found = new List<AsapMessage>();

        var session = await OpenSessionAsync(sessionNo, found, cancellationToken).ConfigureAwait(false);

        if (session is null)
        {
            return Result<PosReceipt>.Failure(found);
        }

        var station = await context.Set<PosStation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == session.StationCode, cancellationToken)
            .ConfigureAwait(false);

        if (station is null)
        {
            return Result<PosReceipt>.Failure(
                messages.Render(PosMessages.StationNotFound, Args(("StationCode", session.StationCode))));
        }

        if (lines.Count == 0)
        {
            return Result<PosReceipt>.Failure(
                messages.Render(PosMessages.ReceiptHasNoLines, Args(("SessionNo", session.No))));
        }

        var items = await ResolveItemsAsync(lines, cancellationToken).ConfigureAwait(false);
        var discountLimit = await DiscountLimitAsync(cancellationToken).ConfigureAwait(false);

        // The discount limit is not enforced here. Nothing has been agreed with anybody yet, and
        // refusing to set a basket down is a strange thing for a till to do; it is asked again,
        // and answered by a supervisor, at the moment the money is taken.
        var units = await ResolveUnitsAsync(lines, items, cancellationToken).ConfigureAwait(false);

        var built = BuildLines(lines, items, units, decimal.MaxValue, original: null, held: null, found);

        _ = discountLimit;

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PosReceipt>.Failure(found);
        }

        var onSession = await context.Set<PosReceipt>()
            .CountAsync(r => r.SessionId == session.Id, cancellationToken)
            .ConfigureAwait(false);

        var receipt = new PosReceipt
        {
            // Suffixed rather than separated by a slash: this handle travels in a URL path when
            // the sale is recalled, and a slash there is a path segment however it is encoded.
            No = $"{session.No}-P{onSession + 1}",
            SessionId = session.Id,
            StationCode = station.Code,
            CustomerNo = customerNo ?? station.DefaultCustomerNo,
            CustomerName = parkedAs ?? station.Name,
            LocationCode = station.LocationCode,
            TakenAtUtc = clock.UtcNow,
            BusinessDate = session.BusinessDate,
            Status = PosReceiptStatus.Parked,
            ParkedAs = parkedAs,
            CashierId = session.CashierId,
        };

        foreach (var line in built)
        {
            receipt.Lines.Add(new PosReceiptLine
            {
                LineNo = line.LineNo,
                Type = line.Type,
                ItemNo = line.Type is PosLineType.Item ? line.No : null,
                AccountNo = line.Type is PosLineType.GlAccount ? line.No : null,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitCode = line.UnitCode,
                VariantCode = line.VariantCode,
                QuantityPerUnit = line.QuantityPerUnit,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                TaxCode = line.TaxCode,
            });
        }

        context.Set<PosReceipt>().Add(receipt);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Parked {ReceiptNo} at {StationCode} with {LineCount} line(s).",
            receipt.No,
            receipt.StationCode,
            receipt.Lines.Count);

        return Result<PosReceipt>.Success(receipt, found);
    }

    /// <summary>Everything set aside and unpaid at a till.</summary>
    /// <param name="sessionNo">The session to look at.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The parked sales, oldest first, which is the order a queue was joined in.</returns>
    public async Task<IReadOnlyList<PosReceipt>> ParkedAsync(
        string sessionNo,
        CancellationToken cancellationToken = default)
    {
        var session = await context.Set<PosSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.No == sessionNo, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            return [];
        }

        return await context.Set<PosReceipt>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .Where(r => r.SessionId == session.Id && r.Status == PosReceiptStatus.Parked)
            .OrderBy(r => r.TakenAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads a parked sale back so the till can carry on with it.</summary>
    /// <param name="receiptNo">The parked sale's handle.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The parked sale, or the reason it could not be recalled.</returns>
    public async Task<Result<PosReceipt>> RecallAsync(
        string receiptNo,
        CancellationToken cancellationToken = default)
    {
        var receipt = await context.Set<PosReceipt>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.No == receiptNo, cancellationToken)
            .ConfigureAwait(false);

        return Parked(receipt, receiptNo) ?? Result<PosReceipt>.Success(receipt!);
    }

    /// <summary>
    /// Throws away a parked sale.
    /// </summary>
    /// <remarks>
    /// Voided rather than deleted. A till that can make transactions disappear is a till nobody
    /// can audit, and "we found forty parked sales thrown away on one shift" is a sentence
    /// somebody needs to be able to say.
    /// </remarks>
    /// <param name="receiptNo">The parked sale's handle.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The voided sale, or the reason it could not be thrown away.</returns>
    public async Task<Result<PosReceipt>> VoidAsync(
        string receiptNo,
        CancellationToken cancellationToken = default)
    {
        var receipt = await context.Set<PosReceipt>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.No == receiptNo, cancellationToken)
            .ConfigureAwait(false);

        if (Parked(receipt, receiptNo) is { } refusal)
        {
            return refusal;
        }

        receipt!.Status = PosReceiptStatus.Voided;
        receipt.ParkedAs = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Voided parked sale {ReceiptNo}.", receiptNo);

        return Result<PosReceipt>.Success(receipt);
    }

    private Result<PosReceipt>? Parked(PosReceipt? receipt, string receiptNo)
    {
        if (receipt is null)
        {
            return Result<PosReceipt>.Failure(
                messages.Render(PosMessages.ReceiptNotFound, Args(("ReceiptNo", receiptNo))));
        }

        return receipt.Status is PosReceiptStatus.Parked
            ? null
            : Result<PosReceipt>.Failure(
                messages.Render(
                    PosMessages.ReceiptNotParked,
                    Args(("ReceiptNo", receiptNo), ("Status", receipt.Status.ToString()))));
    }

    private async Task<PosSession?> OpenSessionAsync(
        string sessionNo,
        List<AsapMessage> found,
        CancellationToken cancellationToken)
    {
        var session = await context.Set<PosSession>()
            .FirstOrDefaultAsync(s => s.No == sessionNo, cancellationToken)
            .ConfigureAwait(false);

        if (session is null)
        {
            found.Add(messages.Render(PosMessages.SessionNotFound, Args(("SessionNo", sessionNo))));

            return null;
        }

        if (!session.IsOpen)
        {
            found.Add(messages.Render(
                PosMessages.SessionClosed,
                Args(("SessionNo", session.No), ("ClosedAt", session.ClosedAtUtc))));

            return null;
        }

        return session;
    }

    /// <summary>Loads the receipt a return is being made against, when one was named.</summary>
    private async Task<PosReceipt?> OriginalAsync(
        string? returnsReceiptNo,
        List<AsapMessage> found,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(returnsReceiptNo))
        {
            return null;
        }

        var original = await context.Set<PosReceipt>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.No == returnsReceiptNo, cancellationToken)
            .ConfigureAwait(false);

        if (original is null)
        {
            found.Add(messages.Render(
                PosMessages.ReceiptNotFound,
                Args(("ReceiptNo", returnsReceiptNo))));

            return null;
        }

        if (original.IsReturn)
        {
            found.Add(messages.Render(
                PosMessages.ReturnAgainstReturn,
                Args(("ReceiptNo", returnsReceiptNo))));

            return null;
        }

        return original;
    }

    /// <summary>
    /// Refuses to take back more than was sold.
    /// </summary>
    /// <remarks>
    /// Counted against everything already returned on that receipt, not just this transaction.
    /// Checking only the receipt in hand lets somebody return two, then two more, then two more,
    /// against a sale of two -- which is the whole trick, and it is not a clever one.
    /// </remarks>
    private async Task CheckReturnAsync(
        PosReceipt original,
        IReadOnlyList<BuiltLine> lines,
        IReadOnlySet<string>? held,
        List<AsapMessage> found,
        CancellationToken cancellationToken)
    {
        var alreadyBack = await context.Set<PosReceipt>()
            .AsNoTracking()
            .Where(r => r.ReturnsReceiptNo == original.No && r.Status == PosReceiptStatus.Posted)
            .SelectMany(static r => r.Lines)
            .GroupBy(static l => l.ItemNo)
            .Select(static g => new { ItemNo = g.Key, Quantity = g.Sum(static l => l.Quantity) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Returned lines carry negative quantities, so the sum is negative and the magnitude is
        // what came back.
        var returned = alreadyBack.ToDictionary(
            static x => x.ItemNo ?? string.Empty,
            static x => -x.Quantity,
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines.Where(static l => l.Quantity < 0m))
        {
            var sold = original.Lines
                .Where(l => string.Equals(l.ItemNo, line.No, StringComparison.OrdinalIgnoreCase))
                .Sum(static l => l.Quantity);

            var back = returned.GetValueOrDefault(line.No);
            var remaining = sold - back;
            var wanted = -line.Quantity;

            if (wanted <= remaining)
            {
                continue;
            }

            found.Add(Raise(
                PosMessages.ReturnExceedsSale,
                Args(
                    ("ItemNo", line.No),
                    ("ReceiptNo", original.No),
                    ("ReturnQuantity", wanted),
                    ("SoldQuantity", sold),
                    ("ReturnedQuantity", back),
                    ("RemainingQuantity", remaining > 0m ? remaining : 0m)),
                held,
                MessageTarget.OnField($"Lines[{line.LineNo}]")));
        }
    }

    /// <summary>What a line came out as once the catalogue and the limits had their say.</summary>
    private sealed record BuiltLine(
        int LineNo,
        PosLineType Type,
        string No,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal DiscountPercent,
        string? TaxCode)
    {
        /// <summary>The unit it was rung in, which is what the receipt prints.</summary>
        public string? UnitCode { get; init; }

        /// <summary>The variant sold, on an item that has them.</summary>
        public string? VariantCode { get; init; }

        /// <summary>
        /// How many base units that unit held when it was rung.
        /// </summary>
        /// <remarks>
        /// Kept on the line rather than looked up again, for the same reason the cost is: a case
        /// of twelve becoming a case of six next year must not restate what a customer bought.
        /// </remarks>
        public decimal QuantityPerUnit { get; init; } = 1m;

        /// <summary>What the goods cost per unit, or null on a line with no goods behind it.</summary>
        public decimal? UnitCostAtSale { get; init; }

        /// <summary>The offer that applied, when one did.</summary>
        public string? OfferCode { get; init; }

        /// <summary>What that offer took off, in money.</summary>
        public decimal OfferDiscountAmount { get; init; }

        /// <summary>What each unit goes for before an offer is considered.</summary>
        public decimal NetUnitPrice => UnitPrice * (1m - (DiscountPercent / 100m));

        /// <summary>What the line comes to after every discount, before tax.</summary>
        public decimal LineAmount => (Quantity * NetUnitPrice) - OfferDiscountAmount;

        /// <summary>What the person at the till took off.</summary>
        public decimal DiscountAmount => Quantity * UnitPrice * (DiscountPercent / 100m);
    }

    /// <summary>What the money side of the receipt came to.</summary>
    private sealed record Settlement(
        string CustomerNo,
        string CustomerName,
        decimal NetAmount,
        decimal DiscountAmount,
        decimal PromotionAmount,
        decimal TaxAmount,
        decimal RoundingAmount,
        decimal TotalAmount,
        decimal ChangeGiven,
        IReadOnlyList<PosTenderRequest> Tenders);

    private List<BuiltLine> BuildLines(
        IReadOnlyList<PosLineRequest> lines,
        IReadOnlyDictionary<string, Item> items,
        UnitLookup units,
        decimal discountLimit,
        PosReceipt? original,
        IReadOnlySet<string>? held,
        List<AsapMessage> found)
    {
        var built = new List<BuiltLine>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNo = index + 1;
            var target = MessageTarget.OnField($"Lines[{lineNo}]");

            var arguments = Args(("LineNo", lineNo), ("ItemNo", line.No));

            if (line.Quantity == 0m)
            {
                found.Add(messages.Render(PosMessages.QuantityZero, arguments, target));
                continue;
            }

            Item? item = null;

            // What the cashier rang, before any unit is applied to it. A case of twelve is rung
            // as one and stored as twelve; this is the one.
            var rung = line.Quantity;
            var unitCode = line.UnitCode;
            var perUnit = 1m;

            if (line.Type is PosLineType.Item)
            {
                if (!items.TryGetValue(line.No, out item))
                {
                    found.Add(messages.Render(PosMessages.ItemNotFound, arguments, target));
                    continue;
                }

                // Withdrawn from use. A till is the easiest place in the system to sell something
                // from -- it takes one scan -- so it is the place this most needs asking, and it
                // was the one place nothing did.
                if (item.IsBlocked && line.Quantity > 0m)
                {
                    arguments["Description"] = item.Description;

                    found.Add(messages.Render(
                        Inventory.InventoryMessages.ItemBlocked,
                        arguments,
                        target));

                    continue;
                }

                var converted = units.Convert(item, unitCode, rung, arguments, target, messages);

                if (converted.Refusal is { } refusal)
                {
                    found.Add(refusal);
                    continue;
                }

                unitCode = converted.UnitCode;
                perUnit = converted.QuantityPerUnit;
            }

            // Everything from here down is in base units, because that is what stock leaves in
            // and what the price is quoted per. A case of twelve at 24.00 is twelve at 24.00.
            var quantity = rung * perUnit;

            // What it went out at on the receipt being returned against, when there is one.
            var sold = quantity < 0m && original is not null
                ? original.Lines.FirstOrDefault(l =>
                    string.Equals(l.ItemNo, line.No, StringComparison.OrdinalIgnoreCase))
                : null;

            // Zero means the shelf price, which is what a cashier scanning something means --
            // except on a return, where it means what this customer actually paid.
            var unitPrice = line.UnitPrice != 0m
                ? line.UnitPrice
                : sold?.UnitPrice ?? item?.UnitPrice ?? 0m;

            var discountPercent = line.DiscountPercent != 0m
                ? line.DiscountPercent
                : sold?.DiscountPercent ?? 0m;

            var description = line.Description
                ?? item?.Description
                ?? line.No;

            // Only what the cashier keyed. A discount carried back from the original receipt is
            // one a supervisor already approved, and asking again at the refund counter would
            // make returning an offer item harder than buying one.
            if (line.DiscountPercent > discountLimit)
            {
                arguments["DiscountPercent"] = line.DiscountPercent;
                arguments["DiscountLimit"] = discountLimit;

                found.Add(Raise(PosMessages.DiscountAboveLimit, arguments, held, target));
            }

            var net = unitPrice * (1m - (discountPercent / 100m));

            // Said at the till, not found in a margin report next month. A return is not a sale
            // below cost however the arithmetic reads, so only outbound lines are checked.
            if (item is not null && quantity > 0m && item.UnitCost > 0m && net < item.UnitCost)
            {
                found.Add(messages.Render(
                    PosMessages.BelowCost,
                    Args(
                        ("LineNo", lineNo),
                        ("ItemNo", line.No),
                        ("Description", description),
                        ("NetUnitPrice", net),
                        ("UnitCost", item.UnitCost),
                        ("LossPerUnit", item.UnitCost - net),
                        ("Quantity", quantity)),
                    target));
            }

            built.Add(new BuiltLine(
                lineNo,
                line.Type,
                line.No,
                description,
                quantity,
                unitPrice,
                discountPercent,
                line.TaxCode ?? sold?.TaxCode)
            {
                UnitCostAtSale = item?.UnitCost,
                UnitCode = unitCode,
                QuantityPerUnit = perUnit,
                VariantCode = line.VariantCode,
            });
        }

        return built;
    }

    /// <summary>
    /// Lets the promotions engine price the basket.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Server-side, and that is not an implementation detail. A till that worked out its own
    /// offers is a till whose arithmetic nobody can audit and whose margin floor anybody with a
    /// debugger can step over. The screen shows a total so the customer is not kept waiting; what
    /// is charged is decided here.
    /// </para>
    /// <para>
    /// Returns is where this needs care. An offer must not apply to goods coming back — the
    /// customer already had whatever discount they had, and applying today's promotion to a
    /// refund would hand back more than was ever paid.
    /// </para>
    /// </remarks>
    private async Task<List<BuiltLine>> ApplyOffersAsync(
        List<BuiltLine> built,
        IReadOnlyDictionary<string, Item> items,
        PosStation station,
        PosSession session,
        IReadOnlySet<string>? held,
        List<AsapMessage> found,
        CancellationToken cancellationToken)
    {
        var sellable = built
            .Where(static l => l.Type is PosLineType.Item && l.Quantity > 0m)
            .ToList();

        if (sellable.Count == 0)
        {
            return built;
        }

        var running = await offers.RunningAsync(session.BusinessDate, cancellationToken)
            .ConfigureAwait(false);

        if (running.Count == 0)
        {
            return built;
        }

        var floor = await offers.FloorAsync(cancellationToken).ConfigureAwait(false);

        var basket = sellable
            .Select(line => new BasketLine(
                line.LineNo,
                line.No,
                items.GetValueOrDefault(line.No)?.CategoryId,
                line.Quantity,
                line.UnitPrice,
                items.GetValueOrDefault(line.No)?.UnitCost ?? 0m,
                line.DiscountPercent))
            .ToList();

        var context = new BasketContext(
            session.BusinessDate,
            TimeOnly.FromDateTime(clock.UtcNow),
            SalesChannel.PointOfSale,
            station.BranchId);

        var names = items.ToDictionary(
            static i => i.Key,
            static i => (string?)i.Value.Description,
            StringComparer.OrdinalIgnoreCase);

        var priced = promotions.Price(basket, running, context, floor, names, found);

        if (priced.Discounts.Count == 0)
        {
            return built;
        }

        return
        [
            .. built.Select(line =>
            {
                var amount = priced.DiscountOn(line.LineNo);

                if (amount <= 0m)
                {
                    return line;
                }

                return line with
                {
                    OfferCode = priced.Discounts.First(d => d.LineNo == line.LineNo).OfferCode,
                    OfferDiscountAmount = amount,
                };
            }),
        ];
    }

    /// <summary>
    /// Works out what is owed, what was paid, and what comes back.
    /// </summary>
    /// <remarks>
    /// The order matters. Tax is charged on the goods, the total is then rounded to a coin that
    /// exists, and only then is the payment measured against it — because rounding after taking
    /// the money would leave every cash sale a halala out.
    /// </remarks>
    private async Task<Settlement?> SettleAsync(
        PosStation station,
        string? customerNo,
        IReadOnlyList<BuiltLine> lines,
        IReadOnlyList<PosTenderRequest> tenders,
        List<AsapMessage> found,
        CancellationToken cancellationToken)
    {
        var net = lines.Sum(static l => l.LineAmount);
        var discount = lines.Sum(static l => l.DiscountAmount);
        var promotion = lines.Sum(static l => l.OfferDiscountAmount);
        var tax = await TaxAsync(lines, cancellationToken).ConfigureAwait(false);

        var beforeRounding = Round(net + tax);
        var rounding = await RoundCashAsync(beforeRounding, tenders, cancellationToken).ConfigureAwait(false);
        var total = beforeRounding + rounding;

        var effectiveCustomerNo = customerNo ?? station.DefaultCustomerNo;

        var customer = await context.Set<Customer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.No == effectiveCustomerNo, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            found.Add(messages.Render(
                Finance.FinanceMessages.PartyNotFound,
                Args(("PartyNo", effectiveCustomerNo), ("PartyKind", "customer"), ("LineNo", 0))));

            return null;
        }

        var tendered = tenders.Sum(static t => t.Amount);
        var change = tendered - total;

        // Charging a walk-in's shopping to the walk-in account creates a debt owed by a person
        // who has already left, against a customer record shared by everybody who ever paid cash.
        if (customerNo is null
            && tenders.Any(static t => t.Kind is TenderKind.OnAccount))
        {
            found.Add(messages.Render(
                PosMessages.OnAccountNeedsCustomer,
                Args(
                    ("Amount", tenders.Where(static t => t.Kind is TenderKind.OnAccount).Sum(static t => t.Amount)),
                    ("CustomerNo", effectiveCustomerNo))));
        }

        // Which way the money is going decides everything below, so it is asked first. A refund
        // is not a sale that happens to be negative: nobody hands change back on money handed
        // back, and treating an over-refund as change owed is how a till comes to look for a card
        // tender that is not there.
        if (total < 0m)
        {
            if (change != 0m)
            {
                found.Add(messages.Render(
                    PosMessages.RefundMismatch,
                    Args(
                        ("TotalAmount", -total),
                        ("TenderedAmount", -tendered),
                        ("DifferenceAmount", change))));
            }
        }
        else if (change < 0m)
        {
            found.Add(messages.Render(
                PosMessages.Underpaid,
                Args(
                    ("TotalAmount", total),
                    ("TenderedAmount", tendered),
                    ("OutstandingAmount", -change))));
        }
        else if (change > 0m)
        {
            // Only notes and coins can come back. A card charged for more than the bill is not
            // change, it is an overcharge somebody has to ring the acquirer about.
            var cash = tenders.Where(static t => t.Kind is TenderKind.Cash).Sum(static t => t.Amount);

            if (cash < change)
            {
                // There is one, because cash alone can never leave less cash than was offered.
                // Guarded anyway: a crash at a till is the worst possible way to learn otherwise.
                var offending = tenders.FirstOrDefault(static t => t.Kind is not TenderKind.Cash);

                found.Add(messages.Render(
                    PosMessages.NoChangeFromTender,
                    Args(
                        ("TenderKind", offending.Kind.ToString()),
                        ("TenderedAmount", tendered),
                        ("TotalAmount", total),
                        ("ChangeGiven", change))));
            }
        }

        return new Settlement(
            customer.No,
            customer.Name,
            Round(net),
            Round(discount),
            Round(promotion),
            tax,
            rounding,
            total,
            Round(Math.Max(change, 0m)),
            tenders);
    }

    /// <summary>
    /// Takes the goods off the shelf, valued by the costing engine.
    /// </summary>
    /// <remarks>
    /// The price on the receipt has nothing to do with it. What leaves carries whatever the
    /// costing engine says it cost, which is the only reason a margin means anything.
    /// </remarks>
    private async Task<decimal> MoveStockAsync(
        PosReceipt receipt,
        PosStation station,
        IReadOnlyList<BuiltLine> lines,
        IReadOnlySet<string>? held,
        string? overrideReason,
        List<AsapMessage> found,
        CancellationToken cancellationToken)
    {
        var movements = lines
            .Where(static l => l.Type is PosLineType.Item)
            .Select(l => new StockMovementRequest(
                ItemNo: l.No,
                LocationCode: receipt.LocationCode,

                // Negative sells, positive takes back. The sign on the line already says which.
                Quantity: -l.Quantity,

                // Zero, because the engine values what leaves. A price here would be the customer
                // paying for the goods and the company recording that as what they cost.
                UnitCost: 0m,
                EntryType: l.Quantity > 0m ? ItemLedgerEntryType.Sale : ItemLedgerEntryType.SalesReturn,
                SalesAmount: l.LineAmount,

                // Carried from the line. A till that could not say which size would be unable to
                // sell anything an item has variants for.
                VariantCode: l.VariantCode,

                // The till's own shelf, stated once in station setup. A cashier took the goods off
                // the shop floor and cannot say where that is on a warehouse map, so this is not
                // the guess the bin rules refuse -- somebody wrote it down in advance.
                BinCode: station.PickBinCode))
            .ToList();

        if (movements.Count == 0)
        {
            return 0m;
        }

        // Said here rather than left to Inventory, whose refusal tells the reader to name a bin.
        // That is sound advice on a warehouse journal and useless at a till, where the person
        // reading it has a queue and no field to answer with.
        if (string.IsNullOrWhiteSpace(station.PickBinCode)
            && await LocationTracksBinsAsync(receipt.LocationCode, cancellationToken).ConfigureAwait(false))
        {
            found.Add(messages.Render(
                PosMessages.TillHasNoPickBin,
                Args(("StationCode", station.Code), ("Location", receipt.LocationCode))));

            return 0m;
        }

        var allowsNegative = await setup
            .GetAsync<bool>(
                $"{Inventory.InventoryModule.Id}.Costing.AllowNegativeInventory",
                cancellationToken)
            .ConfigureAwait(false);

        var posted = await stock
            .PostAsync(
                movements,
                receipt.BusinessDate,
                "POS",
                receipt.No,
                allowsNegative,
                held,
                overrideReason,
                cancellationToken)
            .ConfigureAwait(false);

        found.AddRange(posted.Messages);

        // Reported as a positive cost, which is how anybody reading a receipt thinks about it.
        return posted.Failed ? 0m : -posted.Value.CostAmount;
    }

    /// <summary>
    /// Posts the money: revenue, tax, rounding and whatever was handed over.
    /// </summary>
    /// <remarks>
    /// Revenue goes in at list with the discount beside it as a contra, and both carry the tax
    /// code. Neither line alone is the taxable amount, so taxing each and letting them offset is
    /// what leaves tax charged on what the customer actually pays — and leaves the discount
    /// visible in the P&amp;L instead of buried in a lower price.
    /// </remarks>
    private async Task<Result<Finance.Posting.PostingReceipt>> PostMoneyAsync(
        PosReceipt receipt,
        IReadOnlyList<BuiltLine> lines,
        Settlement settled,
        string? overrideReason,
        CancellationToken cancellationToken)
    {
        var revenueAccount = await AccountAsync($"{Sales.SalesModule.Id}.Posting.RevenueAccount", cancellationToken)
            .ConfigureAwait(false);

        var discountAccount = await AccountAsync($"{Sales.SalesModule.Id}.Posting.DiscountAccount", cancellationToken)
            .ConfigureAwait(false);

        var promotionAccount = await AccountAsync(
                $"{Promotions.PromotionsModule.Id}.Posting.DiscountAccount",
                cancellationToken)
            .ConfigureAwait(false);

        var journal = new List<PostJournalLine>();

        foreach (var line in lines)
        {
            var account = line.Type is PosLineType.GlAccount ? line.No : revenueAccount;

            if (string.IsNullOrWhiteSpace(account))
            {
                continue;
            }

            var showDiscount = line.DiscountAmount != 0m && !string.IsNullOrWhiteSpace(discountAccount);

            var showPromotion = line.OfferDiscountAmount != 0m
                                && !string.IsNullOrWhiteSpace(promotionAccount);

            // Revenue goes in at what the shelf said, and everything taken off it stands beside
            // it as a contra. That is what lets somebody read a month and answer both "what did
            // we sell" and "what did we give away, and to which campaign" -- questions a netted
            // down revenue line cannot answer at all.
            var atList = line.LineAmount
                         + (showDiscount ? line.DiscountAmount : 0m)
                         + (showPromotion ? line.OfferDiscountAmount : 0m);

            journal.Add(new PostJournalLine(
                account,
                -atList,
                line.Description,
                TaxCode: line.TaxCode));

            if (showDiscount)
            {
                journal.Add(new PostJournalLine(
                    discountAccount!,
                    line.DiscountAmount,
                    $"{line.Description} — discount",
                    TaxCode: line.TaxCode));
            }

            if (showPromotion)
            {
                // Every contra carries the tax code too. None of these lines alone is the taxable
                // amount, so taxing each and letting them offset is what leaves tax charged on
                // what the customer actually pays.
                journal.Add(new PostJournalLine(
                    promotionAccount!,
                    line.OfferDiscountAmount,
                    $"{line.Description} — {line.OfferCode}",
                    TaxCode: line.TaxCode));
            }
        }

        if (settled.RoundingAmount != 0m)
        {
            var roundingAccount = await AccountAsync($"{PosModule.Id}.Posting.RoundingAccount", cancellationToken)
                .ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(roundingAccount))
            {
                // The customer pays the rounded total, so the difference is the company's gain or
                // loss and belongs in the P&L rather than quietly inside revenue.
                journal.Add(new PostJournalLine(
                    roundingAccount,
                    -settled.RoundingAmount,
                    $"{receipt.No} — cash rounding"));
            }
        }

        var tenderLines = await TenderLinesAsync(receipt, settled, cancellationToken).ConfigureAwait(false);

        if (tenderLines.Failed)
        {
            return Result<Finance.Posting.PostingReceipt>.FailureFrom(tenderLines);
        }

        journal.AddRange(tenderLines.Value);

        return await documents
            .PostAsync(
                new DocumentPosting(
                    BatchCode: receipt.No,
                    Lines: journal,
                    SourceCode: "POS",

                    // The till owns the revenue, cash and tax accounts it writes to. Nobody keyed
                    // this; a queue of people paid for their shopping.
                    IsManualEntry: false,

                    // Stated, because a cash sale names no customer anywhere. Left to inference,
                    // the discount contra would be read as reclaimable input tax by its sign.
                    PartyKind: PartyKind.Customer,
                    DocumentType: GlDocumentType.PosReceipt,
                    DocumentNo: receipt.No,
                    Description: $"{receipt.StationCode} — {receipt.No}",

                    // The shop the till stands in. This is the whole of branch reporting for a
                    // retailer: without it every sale in the chain reports at head office,
                    // because head office is where the software runs.
                    BranchId: await branches
                        .BranchOfAsync(receipt.StationCode, cancellationToken)
                        .ConfigureAwait(false),
                    OverrideReason: overrideReason,

                    // The trading day, not the calendar one. A late shop still selling at one in
                    // the morning is having Saturday night, and a receipt that posts to Sunday
                    // puts takings in a day the shop will not recognise when it reconciles them.
                    PostingDate: receipt.BusinessDate),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Turns what was handed over into ledger lines.
    /// </summary>
    /// <remarks>
    /// Cash is debited net of change, because the drawer keeps the difference and not the note.
    /// Everything else is debited for what it was charged. An on-account tender is the only one
    /// that names a party, and it is the only one that leaves a debt behind.
    /// </remarks>
    private async Task<Result<List<PostJournalLine>>> TenderLinesAsync(
        PosReceipt receipt,
        Settlement settled,
        CancellationToken cancellationToken)
    {
        var journal = new List<PostJournalLine>();

        foreach (var group in settled.Tenders.GroupBy(static t => t.Kind))
        {
            var amount = group.Sum(static t => t.Amount);

            if (group.Key is TenderKind.Cash)
            {
                amount -= settled.ChangeGiven;
            }

            if (amount == 0m)
            {
                continue;
            }

            if (group.Key is TenderKind.OnAccount)
            {
                journal.Add(new PostJournalLine(
                    settled.CustomerNo,
                    amount,
                    $"{receipt.StationCode} — {receipt.No}",
                    AccountType: JournalAccountType.Customer,
                    ExternalDocumentNo: receipt.No));

                continue;
            }

            var key = $"{PosModule.Id}.Posting.{group.Key}Account";
            var account = await AccountAsync(key, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(account))
            {
                return Result<List<PostJournalLine>>.Failure(
                    messages.Render(
                        PosMessages.NoTenderAccount,
                        Args(("TenderKind", group.Key.ToString()), ("Amount", amount))));
            }

            journal.Add(new PostJournalLine(
                account,
                amount,
                $"{receipt.StationCode} — {receipt.No} — {group.Key}"));
        }

        return Result<List<PostJournalLine>>.Success(journal);
    }

    /// <summary>
    /// Adds what this receipt did to the session's running figures.
    /// </summary>
    /// <remarks>
    /// Kept on the session rather than summed from the receipts at close. A drawer is counted at
    /// the moment somebody is standing in front of it, and a query across a day's receipts is a
    /// slower answer to a question that has to be instant.
    /// </remarks>
    private static void Accumulate(PosSession session, PosReceipt receipt, Settlement settled)
    {
        session.ReceiptCount++;
        session.NetSales += receipt.NetAmount;
        session.TaxAmount += receipt.TaxAmount;

        foreach (var tender in settled.Tenders)
        {
            switch (tender.Kind)
            {
                case TenderKind.Cash when tender.Amount >= 0m:
                    session.CashTendered += tender.Amount;
                    break;

                // A return paid out in cash is money leaving the drawer, which is not negative
                // takings. Counting it as such would make a day of refunds look like a day of
                // fewer sales.
                case TenderKind.Cash:
                    session.CashRefunded += -tender.Amount;
                    break;

                case TenderKind.Card:
                    session.CardTaken += tender.Amount;
                    break;

                case TenderKind.OnAccount:
                    session.OnAccountTaken += tender.Amount;
                    break;

                case TenderKind.Voucher:
                default:
                    break;
            }
        }

        session.ChangeGiven += receipt.ChangeGiven;
    }

    private static PosReceipt NewReceipt(
        PosSession session,
        PosStation station,
        Settlement settled,
        IReadOnlyList<BuiltLine> lines,
        string receiptNo,
        DateOnly businessDate,
        DateTime takenAtUtc,
        string? returnsReceiptNo)
    {
        var receipt = new PosReceipt
        {
            No = receiptNo,
            SessionId = session.Id,
            StationCode = station.Code,
            CustomerNo = settled.CustomerNo,
            CustomerName = settled.CustomerName,
            LocationCode = station.LocationCode,
            TakenAtUtc = takenAtUtc,
            BusinessDate = businessDate,
            ReturnsReceiptNo = returnsReceiptNo,
            NetAmount = settled.NetAmount,
            DiscountAmount = settled.DiscountAmount,
            PromotionAmount = settled.PromotionAmount,
            TaxAmount = settled.TaxAmount,
            RoundingAmount = settled.RoundingAmount,
            ChangeGiven = settled.ChangeGiven,
            CashierId = session.CashierId,
        };

        // The lines are kept as they were rung up, not as they were requested: the price that
        // was actually charged, the description that was actually printed. A receipt that stored
        // what the till was asked for could not be reconciled against the one in somebody's bag.
        foreach (var line in lines)
        {
            receipt.Lines.Add(new PosReceiptLine
            {
                LineNo = line.LineNo,
                Type = line.Type,
                ItemNo = line.Type is PosLineType.Item ? line.No : null,
                AccountNo = line.Type is PosLineType.GlAccount ? line.No : null,
                Description = line.Description,
                Quantity = line.Quantity,
                UnitCode = line.UnitCode,
                VariantCode = line.VariantCode,
                QuantityPerUnit = line.QuantityPerUnit,
                UnitPrice = line.UnitPrice,
                DiscountPercent = line.DiscountPercent,
                OfferCode = line.OfferCode,
                OfferDiscountAmount = line.OfferDiscountAmount,
                UnitCostAtSale = line.UnitCostAtSale,
                TaxCode = line.TaxCode,
            });
        }

        var tenderNo = 0;

        foreach (var tender in settled.Tenders)
        {
            receipt.Tenders.Add(new PosTender
            {
                LineNo = ++tenderNo,
                Kind = tender.Kind,
                Amount = tender.Amount,
                Reference = tender.Reference,
            });
        }

        return receipt;
    }

    /// <summary>What a unit came to on a line, or why it could not.</summary>
    private readonly record struct ConvertedUnit(
        string? UnitCode,
        decimal QuantityPerUnit,
        AsapMessage? Refusal);

    /// <summary>
    /// The units the items on this receipt may be rung in, loaded once for the whole basket.
    /// </summary>
    /// <remarks>
    /// Loaded up front rather than asked per line, because a till is the one place in the system
    /// where a query inside a loop is felt by somebody standing at a counter.
    /// </remarks>
    private sealed record UnitLookup(
        IReadOnlyDictionary<string, ItemUnit> ByItemAndCode,
        IReadOnlyDictionary<string, int> PlacesByCode)
    {
        /// <summary>An empty one, for a basket with no item lines on it.</summary>
        public static UnitLookup Empty { get; } = new(
            new Dictionary<string, ItemUnit>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        /// <summary>The key a unit is held under.</summary>
        /// <param name="itemNo">The item.</param>
        /// <param name="unitCode">The unit.</param>
        /// <returns>The lookup key.</returns>
        public static string Key(string itemNo, string unitCode)
            => itemNo.ToUpperInvariant() + "|" + unitCode.ToUpperInvariant();

        /// <summary>
        /// Works out what one of the named unit holds, and whether the quantity may be keyed in it.
        /// </summary>
        /// <param name="item">The item being rung.</param>
        /// <param name="unitCode">The unit named, or null for the base unit.</param>
        /// <param name="quantity">How many of that unit.</param>
        /// <param name="arguments">The message arguments so far, added to on a refusal.</param>
        /// <param name="target">The field a refusal points at.</param>
        /// <param name="messages">Renders the refusal.</param>
        /// <returns>The unit and its factor, or why the line cannot stand.</returns>
        public ConvertedUnit Convert(
            Item item,
            string? unitCode,
            decimal quantity,
            Dictionary<string, object?> arguments,
            MessageTarget target,
            IMessageCatalog messages)
        {
            var wanted = unitCode?.Trim().ToUpperInvariant();

            // Nothing named, or the base unit named: nothing to convert, and nothing to have set
            // up. An item sold only in the unit it is counted in should need no configuration.
            var isBase = string.IsNullOrEmpty(wanted)
                || string.Equals(wanted, item.BaseUnitOfMeasure, StringComparison.OrdinalIgnoreCase);

            var code = isBase ? item.BaseUnitOfMeasure : wanted!;
            var perUnit = 1m;

            if (!isBase)
            {
                if (!ByItemAndCode.TryGetValue(Key(item.No, code), out var unit))
                {
                    arguments["UnitCode"] = code;
                    arguments["BaseUnit"] = item.BaseUnitOfMeasure;

                    return new ConvertedUnit(null, 1m, messages.Render(
                        Inventory.InventoryMessages.UnitNotSetUpForItem,
                        arguments,
                        target));
                }

                if (unit.QuantityPerUnit <= 0m)
                {
                    arguments["UnitCode"] = code;

                    return new ConvertedUnit(null, 1m, messages.Render(
                        Inventory.InventoryMessages.UnitFactorNotUsable,
                        arguments,
                        target));
                }

                code = unit.UnitCode;
                perUnit = unit.QuantityPerUnit;
            }

            // A unit nobody defined in the company list is not checked, because the base unit is
            // free text on the item and a missing setup must not become a shop that cannot sell.
            if (PlacesByCode.TryGetValue(code, out var places)
                && decimal.Round(quantity, places) != quantity)
            {
                arguments["UnitCode"] = code;
                arguments["DecimalPlaces"] = places;
                arguments["Quantity"] = quantity;

                return new ConvertedUnit(null, 1m, messages.Render(
                    Inventory.InventoryMessages.QuantityTooPrecise,
                    arguments,
                    target));
            }

            return new ConvertedUnit(code, perUnit, null);
        }
    }

    private async Task<UnitLookup> ResolveUnitsAsync(
        IReadOnlyList<PosLineRequest> lines,
        IReadOnlyDictionary<string, Item> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return UnitLookup.Empty;
        }

        var itemIds = items.Values.Select(static i => i.Id).ToList();

        var unitRows = await context.Set<ItemUnit>()
            .AsNoTracking()
            .Where(u => itemIds.Contains(u.ItemId) && u.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byId = items.Values.ToDictionary(static i => i.Id, static i => i.No);

        var byItemAndCode = new Dictionary<string, ItemUnit>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in unitRows)
        {
            if (byId.TryGetValue(row.ItemId, out var itemNo))
            {
                byItemAndCode[UnitLookup.Key(itemNo, row.UnitCode)] = row;
            }
        }

        // Every unit named on the basket, plus every base unit, because a quantity keyed in the
        // base unit is checked for decimal places too -- two and a half of something sold one at
        // a time is the case this exists for.
        var codes = lines
            .Select(static l => l.UnitCode)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c!.Trim().ToUpperInvariant())
            .Concat(items.Values.Select(static i => i.BaseUnitOfMeasure.ToUpperInvariant()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var places = await context.Set<Inventory.Items.UnitOfMeasure>()
            .AsNoTracking()
            .Where(u => codes.Contains(u.Code))
            .ToDictionaryAsync(
                static u => u.Code,
                static u => u.DecimalPlaces,
                StringComparer.OrdinalIgnoreCase,
                cancellationToken)
            .ConfigureAwait(false);

        return new UnitLookup(byItemAndCode, places);
    }

    /// <summary>Whether the till's location tracks stock down to a bin.</summary>
    private Task<bool> LocationTracksBinsAsync(string locationCode, CancellationToken cancellationToken)
        => context.Set<Inventory.Locations.Location>()
            .AsNoTracking()
            .AnyAsync(l => l.Code == locationCode && l.UsesBins, cancellationToken);

    private async Task<Dictionary<string, Item>> ResolveItemsAsync(
        IReadOnlyList<PosLineRequest> lines,
        CancellationToken cancellationToken)
    {
        var itemNos = lines
            .Where(static l => l.Type is PosLineType.Item)
            .Select(static l => l.No)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return itemNos.Count == 0
            ? new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase)
            : await context.Set<Item>()
                .AsNoTracking()
                .Where(i => itemNos.Contains(i.No))
                .ToDictionaryAsync(static i => i.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
                .ConfigureAwait(false);
    }

    /// <summary>Works out the tax on the goods, before rounding touches the total.</summary>
    private async Task<decimal> TaxAsync(
        IReadOnlyList<BuiltLine> lines,
        CancellationToken cancellationToken)
    {
        var codes = lines
            .Select(static l => l.TaxCode)
            .Where(static c => !string.IsNullOrWhiteSpace(c))
            .Select(static c => c!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (codes.Count == 0)
        {
            return 0m;
        }

        var rates = await context.Set<Finance.Tax.TaxCode>()
            .AsNoTracking()
            .Include(c => c.Rates)
            .Where(c => codes.Contains(c.Code) && c.IsActive)
            .ToDictionaryAsync(static c => c.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var today = clock.Today;
        var tax = 0m;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.TaxCode)
                || !rates.TryGetValue(line.TaxCode, out var code))
            {
                continue;
            }

            tax += Round(line.LineAmount * ((code.RateOn(today) ?? 0m) / 100m));
        }

        return tax;
    }

    /// <summary>
    /// Works out what to round a cash total by, if anything.
    /// </summary>
    /// <remarks>
    /// Only cash is rounded, and only when it is the whole payment. A card settles to the halala
    /// perfectly well, and rounding a card total would be taking money for no reason.
    /// </remarks>
    private async Task<decimal> RoundCashAsync(
        decimal total,
        IReadOnlyList<PosTenderRequest> tenders,
        CancellationToken cancellationToken)
    {
        if (tenders.Count == 0 || tenders.Any(static t => t.Kind is not TenderKind.Cash))
        {
            return 0m;
        }

        var increment = await setup
            .GetAsync<decimal>($"{PosModule.Id}.Cash.RoundingIncrement", cancellationToken)
            .ConfigureAwait(false);

        if (increment <= 0m)
        {
            return 0m;
        }

        var rounded = Math.Round(total / increment, MidpointRounding.AwayFromZero) * increment;

        return Round(rounded - total);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private async Task<decimal> DiscountLimitAsync(CancellationToken cancellationToken)
        => await setup
            .GetAsync<decimal>($"{PosModule.Id}.Receipts.DiscountLimitPercent", cancellationToken)
            .ConfigureAwait(false);

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{PosModule.Id}.Receipts.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "POS-RCP";

    private async Task<string?> AccountAsync(string key, CancellationToken cancellationToken)
        => await setup.GetAsync<string>(key, cancellationToken).ConfigureAwait(false);

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }

    private AsapMessage Raise(
        MessageCode code,
        Dictionary<string, object?> arguments,
        IReadOnlySet<string>? held,
        MessageTarget target = default)
    {
        var rendered = messages.Render(code, arguments, target);

        return rendered.OverridePermission is { } permission && held?.Contains(permission) == true
            ? messages.AsOverridden(rendered)
            : rendered;
    }
}
