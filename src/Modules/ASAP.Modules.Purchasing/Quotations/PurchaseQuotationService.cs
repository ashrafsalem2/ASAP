using ASAP.Modules.Finance.Parties;
using ASAP.Modules.Purchasing.Orders;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Numbering;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Purchasing.Quotations;

/// <summary>One thing to ask about.</summary>
/// <param name="Type">Whether it asks about stock or a cost.</param>
/// <param name="No">The item number, or the account number on a cost line.</param>
/// <param name="Quantity">How much is wanted.</param>
/// <param name="Description">What it is, in words.</param>
/// <param name="LocationCode">Where it is wanted.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
public readonly record struct QuotationLineRequest(
    PurchaseLineType Type,
    string No,
    decimal Quantity,
    string? Description = null,
    string? LocationCode = null,
    string? VariantCode = null);

/// <summary>What a vendor says about one line.</summary>
/// <param name="LineNo">The line they are answering.</param>
/// <param name="UnitPrice">What they would charge per unit.</param>
/// <param name="LeadTimeDays">How many days they say it takes.</param>
/// <param name="Note">Anything they said about it.</param>
public readonly record struct QuotationResponseLine(
    int LineNo,
    decimal UnitPrice,
    int? LeadTimeDays = null,
    string? Note = null);

/// <summary>Which vendor wins one line, and why.</summary>
/// <param name="LineNo">The line.</param>
/// <param name="VendorNo">Who wins it.</param>
/// <param name="Reason">
/// Why, which is required whenever the winner is not the cheapest quote.
/// </param>
public readonly record struct QuotationAward(int LineNo, string VendorNo, string? Reason = null);

/// <summary>What one vendor said about one line, as a comparison shows it.</summary>
/// <param name="VendorNo">Who.</param>
/// <param name="VendorName">Their name.</param>
/// <param name="UnitPrice">What they would charge.</param>
/// <param name="LineAmount">What the line comes to at that price.</param>
/// <param name="LeadTimeDays">How long they say it takes.</param>
/// <param name="Note">Anything they said.</param>
/// <param name="IsCheapest">Whether this is the lowest price quoted for the line.</param>
/// <param name="IsFastest">Whether this is the shortest lead time quoted for it.</param>
/// <param name="IsAwarded">Whether this is the quote that won.</param>
public readonly record struct QuotationComparisonCell(
    string VendorNo,
    string VendorName,
    decimal UnitPrice,
    decimal LineAmount,
    int? LeadTimeDays,
    string? Note,
    bool IsCheapest,
    bool IsFastest,
    bool IsAwarded);

/// <summary>One line of a request, with every answer to it.</summary>
/// <param name="LineNo">Its position.</param>
/// <param name="ItemNo">The item or account.</param>
/// <param name="Description">What it is.</param>
/// <param name="Quantity">How much is wanted.</param>
/// <param name="AwardedVendorNo">Who won it, once somebody decided.</param>
/// <param name="AwardReason">Why, where a reason was needed.</param>
/// <param name="AwardedOrderNo">The order it became.</param>
/// <param name="Quotes">What each vendor said, cheapest first.</param>
public readonly record struct QuotationComparisonRow(
    int LineNo,
    string? ItemNo,
    string Description,
    decimal Quantity,
    string? AwardedVendorNo,
    string? AwardReason,
    string? AwardedOrderNo,
    IReadOnlyList<QuotationComparisonCell> Quotes);

/// <summary>
/// Asks several vendors the same question, and helps somebody choose between the answers.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here commits anybody. The vendors are quoting, the company is comparing, and the only
/// thing that becomes real is the order raised from an award.
/// </para>
/// <para>
/// Awarding is per line because real buying is per line. The bolts go to one supplier and the nuts
/// to another; forcing one request onto one vendor would either lose the better price or split the
/// question into several nobody can compare.
/// </para>
/// <para>
/// And the rule the whole thing exists for: awarding to anything other than the cheapest quote is
/// refused unless a reason is given. Choosing the dearer supplier is a legitimate decision -- a
/// fortnight's lead time is worth paying for when the shelf is empty -- but it is also the
/// decision somebody asks about a year later, and a blank field is the difference between an
/// answer and an investigation.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Raises the orders an award becomes.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the request number.</param>
/// <param name="setup">Supplies the number series.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="user">Says who is asking.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records requests and awards.</param>
public sealed class PurchaseQuotationService(
    AsapDbContext context,
    PurchaseOrderService orders,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    ITenantContext tenancy,
    IUserContext user,
    IClock clock,
    ILogger<PurchaseQuotationService> logger)
{
    /// <summary>Raises a request for quotation.</summary>
    /// <param name="lines">What to ask about.</param>
    /// <param name="locationCode">Where the goods are wanted.</param>
    /// <param name="respondByDate">When answers are wanted by.</param>
    /// <param name="neededByDate">When the goods are wanted.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="requisitionNo">The requisition it arose from, where it arose from one.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or every reason it was refused.</returns>
    public async Task<Result<PurchaseQuotationRequest>> CreateAsync(
        IReadOnlyList<QuotationLineRequest> lines,
        string? locationCode = null,
        DateOnly? respondByDate = null,
        DateOnly? neededByDate = null,
        string? description = null,
        string? requisitionNo = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return Result<PurchaseQuotationRequest>.Failure(
                messages.Render(PurchasingMessages.QuotationHasNoLines, Args()));
        }

        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<PurchaseQuotationRequest>.FailureFrom(numbered);
        }

        var request = new PurchaseQuotationRequest
        {
            TenantId = tenancy.TenantId ?? Guid.Empty,
            CompanyId = tenancy.RequireCompanyId(),
            No = numbered.Value,
            Status = QuotationRequestStatus.Draft,
            RequestDate = today,
            RespondByDate = respondByDate,
            NeededByDate = neededByDate,
            LocationCode = locationCode,
            RequisitionNo = requisitionNo,
            Description = description,
            CreatedBy = user.UserId,
        };

        var lineNo = 0;

        foreach (var line in lines)
        {
            request.Lines.Add(new PurchaseQuotationRequestLine
            {
                TenantId = request.TenantId,
                CompanyId = request.CompanyId,
                LineNo = ++lineNo * 10,
                Type = line.Type,
                ItemNo = line.Type is PurchaseLineType.Item ? line.No : null,
                VariantCode = line.Type is PurchaseLineType.Item ? line.VariantCode : null,
                AccountNo = line.Type is PurchaseLineType.GlAccount ? line.No : null,
                Description = line.Description ?? line.No,
                LocationCode = line.LocationCode,
                Quantity = line.Quantity,
            });
        }

        context.Set<PurchaseQuotationRequest>().Add(request);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseQuotationRequest>.Success(request);
    }

    /// <summary>Adds vendors to ask.</summary>
    /// <param name="requestNo">The request.</param>
    /// <param name="vendorNos">Who to ask.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or the reason nobody was added.</returns>
    public async Task<Result<PurchaseQuotationRequest>> InviteAsync(
        string requestNo,
        IReadOnlyList<string> vendorNos,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vendorNos);

        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(requestNo);
        }

        var wanted = vendorNos
            .Where(static v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // One query rather than one per vendor. A tender goes to a handful of suppliers and this
        // is not a hot path, but a query inside a loop is a habit worth not forming.
        var vendors = await context.Set<Vendor>()
            .AsNoTracking()
            .Where(v => wanted.Contains(v.No))
            .ToDictionaryAsync(static v => v.No, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        var found = new List<AsapMessage>();

        foreach (var vendorNo in wanted)
        {
            if (request.Invitations.Any(i =>
                string.Equals(i.VendorNo, vendorNo, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!vendors.TryGetValue(vendorNo, out var vendor))
            {
                found.Add(messages.Render(
                    PurchasingMessages.VendorNotFound,
                    Args(("VendorNo", vendorNo))));

                continue;
            }

            // Added through the set with the key set here, rather than by pushing into the
            // parent's collection. Letting EF work the relationship out from a navigation it is
            // also being asked to load in the same unit of work is how a save ends up arguing
            // with itself.
            context.Set<PurchaseQuotationInvitation>().Add(new PurchaseQuotationInvitation
            {
                TenantId = request.TenantId,
                CompanyId = request.CompanyId,
                PurchaseQuotationRequestId = request.Id,
                VendorNo = vendor.No,
                VendorName = vendor.Name,
            });
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseQuotationRequest>.Failure(found);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseQuotationRequest>.Success(request);
    }

    /// <summary>Marks the request as gone out to the vendors.</summary>
    /// <param name="requestNo">The request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or the reason it could not be sent.</returns>
    public async Task<Result<PurchaseQuotationRequest>> SendAsync(
        string requestNo,
        CancellationToken cancellationToken = default)
    {
        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(requestNo);
        }

        if (request.Invitations.Count == 0)
        {
            // A request nobody was asked is not a request. It would sit in Sent for ever waiting
            // for answers from an empty list.
            return Result<PurchaseQuotationRequest>.Failure(
                messages.Render(PurchasingMessages.QuotationHasNoVendors, Args(("RequestNo", request.No))));
        }

        if (request.Status is QuotationRequestStatus.Draft)
        {
            // The status says it went out and the request date says when. Stamping every
            // invitation as well would only be a second copy of the same fact, kept in a place
            // where it can disagree with the first.
            request.Status = QuotationRequestStatus.Sent;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result<PurchaseQuotationRequest>.Success(request);
    }

    /// <summary>
    /// Records what one vendor said.
    /// </summary>
    /// <remarks>
    /// Only from a vendor who was asked. A quote from somebody nobody invited is either a mistake
    /// or a different conversation, and letting it into the comparison would let a vendor add
    /// themselves to a tender.
    /// </remarks>
    /// <param name="requestNo">The request.</param>
    /// <param name="vendorNo">Who answered.</param>
    /// <param name="lines">What they said about each line.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or every reason the answer was refused.</returns>
    public async Task<Result<PurchaseQuotationRequest>> RespondAsync(
        string requestNo,
        string vendorNo,
        IReadOnlyList<QuotationResponseLine> lines,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(requestNo);
        }

        var invitation = request.Invitations.FirstOrDefault(i =>
            string.Equals(i.VendorNo, vendorNo, StringComparison.OrdinalIgnoreCase));

        if (invitation is null)
        {
            return Result<PurchaseQuotationRequest>.Failure(messages.Render(
                PurchasingMessages.VendorWasNotAsked,
                Args(("RequestNo", request.No), ("VendorNo", vendorNo))));
        }

        var byLineNo = request.Lines.ToDictionary(static l => l.LineNo);
        var found = new List<AsapMessage>();

        foreach (var line in lines)
        {
            if (!byLineNo.TryGetValue(line.LineNo, out var requestLine))
            {
                found.Add(messages.Render(
                    PurchasingMessages.QuotationLineNotFound,
                    Args(("RequestNo", request.No), ("LineNo", line.LineNo))));

                continue;
            }

            var existing = request.Responses.FirstOrDefault(r =>
                r.LineNo == line.LineNo
                && string.Equals(r.VendorNo, invitation.VendorNo, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                existing = new PurchaseQuotationResponse
                {
                    TenantId = request.TenantId,
                    CompanyId = request.CompanyId,
                    PurchaseQuotationRequestId = request.Id,
                    LineNo = line.LineNo,
                    VendorNo = invitation.VendorNo,
                };

                // Added through the set only. EF puts it into the request's collection itself
                // once the key is set, and adding it here as well would leave the same answer in
                // the comparison twice.
                context.Set<PurchaseQuotationResponse>().Add(existing);
            }

            existing.UnitPrice = line.UnitPrice;
            existing.LeadTimeDays = line.LeadTimeDays;
            existing.Note = line.Note;
            existing.LineAmount = requestLine.Quantity * line.UnitPrice;
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseQuotationRequest>.Failure(found);
        }

        invitation.RespondedAtUtc = clock.UtcNow;
        invitation.DeclinedReason = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseQuotationRequest>.Success(request);
    }

    /// <summary>Records that a vendor is not quoting.</summary>
    /// <param name="requestNo">The request.</param>
    /// <param name="vendorNo">Who said no.</param>
    /// <param name="reason">Why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or the reason it could not be recorded.</returns>
    public async Task<Result<PurchaseQuotationRequest>> DeclineAsync(
        string requestNo,
        string vendorNo,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(requestNo);
        }

        var invitation = request.Invitations.FirstOrDefault(i =>
            string.Equals(i.VendorNo, vendorNo, StringComparison.OrdinalIgnoreCase));

        if (invitation is null)
        {
            return Result<PurchaseQuotationRequest>.Failure(messages.Render(
                PurchasingMessages.VendorWasNotAsked,
                Args(("RequestNo", request.No), ("VendorNo", vendorNo))));
        }

        invitation.DeclinedReason = reason ?? "Declined";
        invitation.RespondedAtUtc = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseQuotationRequest>.Success(request);
    }

    /// <summary>
    /// Decides which vendor wins each line.
    /// </summary>
    /// <remarks>
    /// Awarding to anything other than the cheapest quote needs a reason. That is the whole point
    /// of the exercise: choosing the dearer supplier is legitimate and often right, and it is also
    /// the decision somebody asks about a year later.
    /// </remarks>
    /// <param name="requestNo">The request.</param>
    /// <param name="awards">Who wins each line, and why where a reason is needed.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or every reason the award was refused.</returns>
    public async Task<Result<PurchaseQuotationRequest>> AwardAsync(
        string requestNo,
        IReadOnlyList<QuotationAward> awards,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(awards);

        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(requestNo);
        }

        var byLineNo = request.Lines.ToDictionary(static l => l.LineNo);
        var found = new List<AsapMessage>();

        foreach (var award in awards)
        {
            if (!byLineNo.TryGetValue(award.LineNo, out var line))
            {
                found.Add(messages.Render(
                    PurchasingMessages.QuotationLineNotFound,
                    Args(("RequestNo", request.No), ("LineNo", award.LineNo))));

                continue;
            }

            if (line.AwardedOrderNo is { Length: > 0 })
            {
                // The award already became an order. Changing it now would leave the order
                // pointing at a decision that no longer says what it says.
                found.Add(messages.Render(
                    PurchasingMessages.QuotationAlreadyOrdered,
                    Args(
                        ("RequestNo", request.No),
                        ("LineNo", line.LineNo),
                        ("OrderNo", line.AwardedOrderNo))));

                continue;
            }

            var quotes = request.Responses.Where(r => r.LineNo == award.LineNo).ToList();

            var winner = quotes.FirstOrDefault(r =>
                string.Equals(r.VendorNo, award.VendorNo, StringComparison.OrdinalIgnoreCase));

            if (winner is null)
            {
                found.Add(messages.Render(
                    PurchasingMessages.VendorDidNotQuote,
                    Args(
                        ("RequestNo", request.No),
                        ("LineNo", award.LineNo),
                        ("VendorNo", award.VendorNo))));

                continue;
            }

            var cheapest = quotes.Min(static r => r.UnitPrice);

            if (winner.UnitPrice > cheapest && string.IsNullOrWhiteSpace(award.Reason))
            {
                var best = quotes.First(r => r.UnitPrice == cheapest);

                found.Add(messages.Render(
                    PurchasingMessages.DearerQuoteNeedsAReason,
                    Args(
                        ("RequestNo", request.No),
                        ("LineNo", award.LineNo),
                        ("VendorNo", award.VendorNo),
                        ("UnitPrice", winner.UnitPrice),
                        ("CheapestVendorNo", best.VendorNo),
                        ("CheapestUnitPrice", cheapest),
                        ("Difference", winner.UnitPrice - cheapest)),
                    MessageTarget.OnField($"Lines[{award.LineNo}]")));

                continue;
            }

            line.AwardedVendorNo = winner.VendorNo;
            line.AwardedUnitCost = winner.UnitPrice;
            line.AwardReason = award.Reason;
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseQuotationRequest>.Failure(found);
        }

        if (!request.HasUnawardedLines)
        {
            request.Status = QuotationRequestStatus.Awarded;
        }
        else if (request.Status is QuotationRequestStatus.Sent)
        {
            request.Status = QuotationRequestStatus.Closed;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Awarded {Count} line(s) on {RequestNo}.",
            awards.Count,
            request.No);

        return Result<PurchaseQuotationRequest>.Success(request);
    }

    /// <summary>
    /// Turns one vendor's awarded lines into an order.
    /// </summary>
    /// <remarks>
    /// Called once per winning vendor, and it carries the price they quoted. That is the one place
    /// this differs from a requisition: a requisition's estimate was a guess and the order needed a
    /// real price typed, while a quote <em>is</em> the real price and typing it again would only
    /// create a way for it to differ from what was agreed.
    /// </remarks>
    /// <param name="requestNo">The request.</param>
    /// <param name="vendorNo">Whose awarded lines to order.</param>
    /// <param name="expectedReceiptDate">When the goods are expected.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order, or every reason it was refused.</returns>
    public async Task<Result<PurchaseOrder>> OrderAsync(
        string requestNo,
        string vendorNo,
        DateOnly? expectedReceiptDate = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        CancellationToken cancellationToken = default)
    {
        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return Result<PurchaseOrder>.FailureFrom(NotFound(requestNo));
        }

        var winning = request.Lines
            .Where(l => string.Equals(l.AwardedVendorNo, vendorNo, StringComparison.OrdinalIgnoreCase)
                && l.AwardedOrderNo is null)
            .OrderBy(static l => l.LineNo)
            .ToList();

        if (winning.Count == 0)
        {
            return Result<PurchaseOrder>.Failure(messages.Render(
                PurchasingMessages.NothingAwardedToOrder,
                Args(("RequestNo", request.No), ("VendorNo", vendorNo))));
        }

        var order = await orders
            .CreateAsync(
                vendorNo,
                [
                    .. winning.Select(static l => new PurchaseOrderLineRequest(
                        l.Type,
                        l.Type is PurchaseLineType.Item ? l.ItemNo! : l.AccountNo!,
                        l.Quantity,

                        // What they quoted. Retyping it would only create a way for the order to
                        // disagree with the quote it came from.
                        l.AwardedUnitCost ?? 0m,
                        l.Description,
                        LocationCode: l.LocationCode,
                        VariantCode: l.VariantCode)),
                ],
                request.LocationCode,
                expectedReceiptDate ?? request.NeededByDate,
                $"{request.No}{(request.Description is { Length: > 0 } d ? $" — {d}" : string.Empty)}",
                heldOverridePermissions: heldOverridePermissions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (order.Failed)
        {
            return order;
        }

        foreach (var line in winning)
        {
            line.AwardedOrderNo = order.Value.No;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Request {RequestNo} became order {OrderNo} for {VendorNo}.",
            request.No,
            order.Value.No,
            vendorNo);

        return order;
    }

    /// <summary>Abandons a request.</summary>
    /// <param name="requestNo">The request.</param>
    /// <param name="reason">Why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or the reason it could not be abandoned.</returns>
    public async Task<Result<PurchaseQuotationRequest>> CancelAsync(
        string requestNo,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return NotFound(requestNo);
        }

        if (request.Lines.Any(static l => l.AwardedOrderNo is { Length: > 0 }))
        {
            return Result<PurchaseQuotationRequest>.Failure(messages.Render(
                PurchasingMessages.QuotationHasOrders,
                Args(("RequestNo", request.No))));
        }

        request.Status = QuotationRequestStatus.Cancelled;
        request.CancellationReason = reason;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseQuotationRequest>.Success(request);
    }

    /// <summary>
    /// The answers, put side by side.
    /// </summary>
    /// <remarks>
    /// Cheapest and fastest are flagged separately and deliberately. They are often different
    /// vendors, and a comparison that showed only money would make the choice look obvious when it
    /// is not.
    /// </remarks>
    /// <param name="requestNo">The request.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>One row per line, with every quote for it.</returns>
    public async Task<IReadOnlyList<QuotationComparisonRow>> CompareAsync(
        string requestNo,
        CancellationToken cancellationToken = default)
    {
        var request = await LoadAsync(requestNo, cancellationToken).ConfigureAwait(false);

        if (request is null)
        {
            return [];
        }

        var names = request.Invitations.ToDictionary(
            static i => i.VendorNo,
            static i => i.VendorName,
            StringComparer.OrdinalIgnoreCase);

        return
        [
            .. request.Lines
                .OrderBy(static l => l.LineNo)
                .Select(line =>
                {
                    var quotes = request.Responses.Where(r => r.LineNo == line.LineNo).ToList();

                    var cheapest = quotes.Count == 0 ? (decimal?)null : quotes.Min(static q => q.UnitPrice);

                    var fastest = quotes.Where(static q => q.LeadTimeDays is not null).ToList() is { Count: > 0 } timed
                        ? timed.Min(static q => q.LeadTimeDays)
                        : null;

                    return new QuotationComparisonRow(
                        line.LineNo,
                        line.ItemNo ?? line.AccountNo,
                        line.Description,
                        line.Quantity,
                        line.AwardedVendorNo,
                        line.AwardReason,
                        line.AwardedOrderNo,
                        [
                            .. quotes
                                .OrderBy(static q => q.UnitPrice)
                                .Select(q => new QuotationComparisonCell(
                                    q.VendorNo,
                                    names.GetValueOrDefault(q.VendorNo) ?? q.VendorNo,
                                    q.UnitPrice,
                                    q.LineAmount,
                                    q.LeadTimeDays,
                                    q.Note,
                                    q.UnitPrice == cheapest,
                                    q.LeadTimeDays is not null && q.LeadTimeDays == fastest,
                                    string.Equals(q.VendorNo, line.AwardedVendorNo, StringComparison.OrdinalIgnoreCase))),
                        ]);
                }),
        ];
    }

    /// <summary>Loads a request with its lines, invitations and answers.</summary>
    /// <param name="requestNo">The number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The request, or null when nothing carries that number.</returns>
    public Task<PurchaseQuotationRequest?> LoadAsync(
        string requestNo,
        CancellationToken cancellationToken = default)
        => context.Set<PurchaseQuotationRequest>()
            .Include(r => r.Lines)
            .Include(r => r.Invitations)
            .Include(r => r.Responses)
            .FirstOrDefaultAsync(r => r.No == requestNo, cancellationToken);

    /// <summary>The requests, newest first.</summary>
    /// <param name="status">One status, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requests.</returns>
    public async Task<IReadOnlyList<PurchaseQuotationRequest>> ListAsync(
        QuotationRequestStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<PurchaseQuotationRequest>()
            .AsNoTracking()
            .Include(r => r.Lines)
            .Include(r => r.Invitations)
            .AsQueryable();

        if (status is { } wanted)
        {
            query = query.Where(r => r.Status == wanted);
        }

        return await query
            .OrderByDescending(r => r.No)
            .Take(200)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Builds the refusal for a number that matches nothing.</summary>
    /// <param name="requestNo">The number that was asked for.</param>
    /// <returns>The failure.</returns>
    public Result<PurchaseQuotationRequest> NotFound(string requestNo)
        => Result<PurchaseQuotationRequest>.Failure(messages.Render(
            PurchasingMessages.QuotationNotFound,
            Args(("RequestNo", requestNo))));

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{PurchasingModule.Id}.Quotations.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "PURCH-RFQ";

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
