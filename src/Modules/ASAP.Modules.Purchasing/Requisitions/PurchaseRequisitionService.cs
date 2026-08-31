using ASAP.Modules.Purchasing.Approvals;
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

namespace ASAP.Modules.Purchasing.Requisitions;

/// <summary>One thing asked for on a new requisition.</summary>
/// <param name="Type">Whether it asks for stock or a cost.</param>
/// <param name="No">The item number, or the account number on a cost line.</param>
/// <param name="Quantity">How much is wanted.</param>
/// <param name="EstimatedUnitCost">What whoever is asking thinks it costs.</param>
/// <param name="Description">What is wanted, in words.</param>
/// <param name="LocationCode">Where it is wanted, when it differs from the requisition.</param>
/// <param name="VariantCode">Which variant, on an item that has them.</param>
/// <param name="SuggestedVendorNo">A vendor somebody has in mind. A suggestion, not a commitment.</param>
public readonly record struct PurchaseRequisitionLineRequest(
    PurchaseLineType Type,
    string No,
    decimal Quantity,
    decimal EstimatedUnitCost = 0m,
    string? Description = null,
    string? LocationCode = null,
    string? VariantCode = null,
    string? SuggestedVendorNo = null);

/// <summary>Which lines of a requisition are going onto an order, and at what price.</summary>
/// <param name="LineNo">The requisition line.</param>
/// <param name="Quantity">How much of it to order.</param>
/// <param name="DirectUnitCost">
/// What the vendor is actually charging. The estimate on the requisition is not used: it was a
/// guess, and an order posts real money.
/// </param>
public readonly record struct RequisitionOrderLineRequest(
    int LineNo,
    decimal Quantity,
    decimal DirectUnitCost);

/// <summary>
/// Takes requests for things to be bought, and turns the approved ones into orders.
/// </summary>
/// <remarks>
/// <para>
/// A requisition names a need rather than a purchase. Who to buy from may not be known, and what
/// it will cost is a guess by whoever is asking -- which is why nothing here posts and nothing
/// here commits the company to anything.
/// </para>
/// <para>
/// One requisition becomes as many orders as it has vendors, so each line counts how much of it
/// has already been ordered. That counter is the only thing standing between a line and being
/// bought twice, and it is checked rather than trusted.
/// </para>
/// <para>
/// Approving a requisition is not approving the orders that come out of it. The approval is
/// measured against an estimate somebody typed; the orders are measured against real prices,
/// through the order's own approval on its own figures. Letting an approved estimate authorise an
/// order at any price would make the estimate the control, and the estimate is the one number on
/// the document nobody has checked.
/// </para>
/// <para>
/// And nobody signs for their own request. That rule is the whole of what an approval is: one that
/// can be given by the person who asked is a checkbox, not a control.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="orders">Raises the orders an approved requisition becomes.</param>
/// <param name="approvals">Says whether an amount needs signing for at all.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="numbers">Issues the requisition number.</param>
/// <param name="setup">Supplies the number series.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="user">Says who is asking, and who is signing.</param>
/// <param name="clock">Supplies today.</param>
/// <param name="logger">Records requisitions.</param>
public sealed class PurchaseRequisitionService(
    AsapDbContext context,
    PurchaseOrderService orders,
    PurchaseApprovalService approvals,
    IMessageCatalog messages,
    INumberSeriesService numbers,
    ISetupService setup,
    ITenantContext tenancy,
    IUserContext user,
    IClock clock,
    ILogger<PurchaseRequisitionService> logger)
{
    /// <summary>
    /// Raises a request for something to be bought.
    /// </summary>
    /// <param name="lines">What is being asked for.</param>
    /// <param name="locationCode">Where the goods are wanted.</param>
    /// <param name="neededByDate">When they are wanted by.</param>
    /// <param name="description">What it is for.</param>
    /// <param name="justification">Why it is needed, which is what an approver reads.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisition, or every reason it was refused.</returns>
    public async Task<Result<PurchaseRequisition>> CreateAsync(
        IReadOnlyList<PurchaseRequisitionLineRequest> lines,
        string? locationCode = null,
        DateOnly? neededByDate = null,
        string? description = null,
        string? justification = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return Result<PurchaseRequisition>.Failure(
                messages.Render(PurchasingMessages.RequisitionHasNoLines, Args()));
        }

        var found = new List<AsapMessage>();

        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Quantity <= 0m)
            {
                found.Add(messages.Render(
                    PurchasingMessages.RequisitionQuantityZero,
                    Args(("LineNo", index + 1), ("ItemNo", lines[index].No)),
                    MessageTarget.OnField($"Lines[{index + 1}]")));
            }
        }

        if (found.Exists(static m => m.IsFailure))
        {
            return Result<PurchaseRequisition>.Failure(found);
        }

        var today = clock.Today;
        var seriesCode = await SeriesCodeAsync(cancellationToken).ConfigureAwait(false);
        var numbered = await numbers.NextAsync(seriesCode, today, cancellationToken).ConfigureAwait(false);

        if (numbered.Failed)
        {
            return Result<PurchaseRequisition>.FailureFrom(numbered);
        }

        var requisition = new PurchaseRequisition
        {
            TenantId = tenancy.TenantId ?? Guid.Empty,
            CompanyId = tenancy.RequireCompanyId(),
            No = numbered.Value,
            Status = PurchaseRequisitionStatus.Draft,
            RequisitionDate = today,
            NeededByDate = neededByDate,
            LocationCode = locationCode,
            Description = description,
            Justification = justification,
            RequestedByUserId = user.UserId,
            RequestedByUserName = user.DisplayName ?? user.UserName,
            CreatedBy = user.UserId,
        };

        var lineNo = 0;

        foreach (var line in lines)
        {
            requisition.Lines.Add(new PurchaseRequisitionLine
            {
                TenantId = requisition.TenantId,
                CompanyId = requisition.CompanyId,
                LineNo = ++lineNo * 10,
                Type = line.Type,
                ItemNo = line.Type is PurchaseLineType.Item ? line.No : null,
                VariantCode = line.Type is PurchaseLineType.Item ? line.VariantCode : null,
                AccountNo = line.Type is PurchaseLineType.GlAccount ? line.No : null,
                Description = line.Description ?? line.No,
                LocationCode = line.LocationCode,
                Quantity = line.Quantity,
                EstimatedUnitCost = line.EstimatedUnitCost,
                SuggestedVendorNo = line.SuggestedVendorNo,
            });
        }

        context.Set<PurchaseRequisition>().Add(requisition);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Raised requisition {RequisitionNo} for an estimated {Amount}.",
            requisition.No,
            requisition.EstimatedAmount);

        return Result<PurchaseRequisition>.Success(requisition);
    }

    /// <summary>
    /// Sends a requisition for approval, or approves it where none is needed.
    /// </summary>
    /// <remarks>
    /// A requisition under the company's threshold goes straight through, signed by nobody. That
    /// is not a gap: the threshold is a number somebody chose, and choosing it is the decision.
    /// </remarks>
    /// <param name="requisitionNo">The requisition.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisition, or the reason it could not be submitted.</returns>
    public async Task<Result<PurchaseRequisition>> SubmitAsync(
        string requisitionNo,
        CancellationToken cancellationToken = default)
    {
        var requisition = await LoadAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        if (requisition is null)
        {
            return NotFound(requisitionNo);
        }

        if (requisition.Status is not PurchaseRequisitionStatus.Draft)
        {
            return Result<PurchaseRequisition>.Failure(messages.Render(
                PurchasingMessages.RequisitionNotADraft,
                Args(("RequisitionNo", requisition.No), ("Status", requisition.Status.ToString()))));
        }

        var needsApproval = await approvals
            .NeedsApprovalAsync(requisition.EstimatedAmount, cancellationToken)
            .ConfigureAwait(false);

        if (needsApproval)
        {
            requisition.Status = PurchaseRequisitionStatus.Submitted;
        }
        else
        {
            requisition.Status = PurchaseRequisitionStatus.Approved;
            requisition.ApprovedAmount = requisition.EstimatedAmount;
            requisition.ApprovedAtUtc = clock.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseRequisition>.Success(requisition);
    }

    /// <summary>
    /// Signs for a requisition.
    /// </summary>
    /// <param name="requisitionNo">The requisition.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisition, or the reason it could not be approved.</returns>
    public async Task<Result<PurchaseRequisition>> ApproveAsync(
        string requisitionNo,
        CancellationToken cancellationToken = default)
    {
        var requisition = await LoadAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        if (requisition is null)
        {
            return NotFound(requisitionNo);
        }

        if (requisition.Status is not PurchaseRequisitionStatus.Submitted)
        {
            return Result<PurchaseRequisition>.Failure(messages.Render(
                PurchasingMessages.RequisitionNotAwaitingApproval,
                Args(("RequisitionNo", requisition.No), ("Status", requisition.Status.ToString()))));
        }

        // The rule the whole thing turns on. An approval you can give yourself is not a control.
        if (requisition.RequestedByUserId == user.RequireUserId())
        {
            return Result<PurchaseRequisition>.Failure(messages.Render(
                PurchasingMessages.CannotApproveYourOwnRequisition,
                Args(("RequisitionNo", requisition.No))));
        }

        requisition.Status = PurchaseRequisitionStatus.Approved;
        requisition.ApprovedByUserId = user.UserId;
        requisition.ApprovedByUserName = user.DisplayName ?? user.UserName;
        requisition.ApprovedAtUtc = clock.UtcNow;

        // Frozen at the moment of signing: authority for an amount, not for a document number.
        requisition.ApprovedAmount = requisition.EstimatedAmount;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Requisition {RequisitionNo} approved by {Approver} at an estimated {Amount}.",
            requisition.No,
            requisition.ApprovedByUserName,
            requisition.ApprovedAmount);

        return Result<PurchaseRequisition>.Success(requisition);
    }

    /// <summary>Turns a requisition down.</summary>
    /// <param name="requisitionNo">The requisition.</param>
    /// <param name="reason">Why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisition, or the reason it could not be rejected.</returns>
    public async Task<Result<PurchaseRequisition>> RejectAsync(
        string requisitionNo,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var requisition = await LoadAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        if (requisition is null)
        {
            return NotFound(requisitionNo);
        }

        if (requisition.Status is not PurchaseRequisitionStatus.Submitted)
        {
            return Result<PurchaseRequisition>.Failure(messages.Render(
                PurchasingMessages.RequisitionNotAwaitingApproval,
                Args(("RequisitionNo", requisition.No), ("Status", requisition.Status.ToString()))));
        }

        requisition.Status = PurchaseRequisitionStatus.Rejected;
        requisition.RejectionReason = reason;
        requisition.ApprovedByUserId = user.UserId;
        requisition.ApprovedByUserName = user.DisplayName ?? user.UserName;
        requisition.ApprovedAtUtc = clock.UtcNow;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseRequisition>.Success(requisition);
    }

    /// <summary>
    /// Turns part of an approved requisition into an order for one vendor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called once per vendor. A requisition asking for paper, bolts and a kettle is one question
    /// with three answers, and this is how each answer is given.
    /// </para>
    /// <para>
    /// The prices come from here rather than from the requisition, because the requisition carried
    /// a guess and an order posts real money. The order then goes through its own approval on its
    /// own figures -- an approved requisition is authority to buy the thing, not authority to buy
    /// it at any price.
    /// </para>
    /// </remarks>
    /// <param name="requisitionNo">The requisition.</param>
    /// <param name="vendorNo">Who to buy from.</param>
    /// <param name="lines">
    /// Which lines and at what price, or null for everything still outstanding at its estimate.
    /// </param>
    /// <param name="expectedReceiptDate">When the goods are expected.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The order raised, or every reason it was refused.</returns>
    public async Task<Result<PurchaseOrder>> OrderAsync(
        string requisitionNo,
        string vendorNo,
        IReadOnlyList<RequisitionOrderLineRequest>? lines = null,
        DateOnly? expectedReceiptDate = null,
        IReadOnlySet<string>? heldOverridePermissions = null,
        CancellationToken cancellationToken = default)
    {
        var requisition = await LoadAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        if (requisition is null)
        {
            return Result<PurchaseOrder>.FailureFrom(NotFound(requisitionNo));
        }

        var arguments = Args(
            ("RequisitionNo", requisition.No),
            ("Status", requisition.Status.ToString()));

        // Everything already ordered is a different situation from never approved, and saying
        // the second when the first is true sends somebody looking for a signature that was given
        // weeks ago.
        if (requisition.Status is PurchaseRequisitionStatus.Ordered)
        {
            return Result<PurchaseOrder>.Failure(
                messages.Render(PurchasingMessages.NothingLeftToOrder, arguments));
        }

        if (requisition.Status is not PurchaseRequisitionStatus.Approved)
        {
            return Result<PurchaseOrder>.Failure(
                messages.Render(PurchasingMessages.RequisitionNotApproved, arguments));
        }

        var going = Going(requisition, lines);

        if (going.Count == 0)
        {
            return Result<PurchaseOrder>.Failure(
                messages.Render(PurchasingMessages.NothingLeftToOrder, arguments));
        }

        var refusals = Check(requisition, going);

        if (refusals.Count > 0)
        {
            return Result<PurchaseOrder>.Failure(refusals);
        }

        var order = await orders
            .CreateAsync(
                vendorNo,
                [
                    .. going.Select(static g => new PurchaseOrderLineRequest(
                        g.Line.Type,
                        g.Line.Type is PurchaseLineType.Item ? g.Line.ItemNo! : g.Line.AccountNo!,
                        g.Quantity,
                        g.UnitCost,
                        g.Line.Description,
                        LocationCode: g.Line.LocationCode,
                        VariantCode: g.Line.VariantCode)),
                ],
                requisition.LocationCode,
                expectedReceiptDate ?? requisition.NeededByDate,
                $"{requisition.No}{(requisition.Description is { Length: > 0 } d ? $" — {d}" : string.Empty)}",
                heldOverridePermissions: heldOverridePermissions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (order.Failed)
        {
            return order;
        }

        foreach (var (line, quantity, _) in going)
        {
            line.QuantityOrdered += quantity;
        }

        if (!requisition.HasOutstandingLines)
        {
            requisition.Status = PurchaseRequisitionStatus.Ordered;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Requisition {RequisitionNo} became order {OrderNo} for {VendorNo}.",
            requisition.No,
            order.Value.No,
            vendorNo);

        return order;
    }

    /// <summary>Abandons a requisition before it becomes anything.</summary>
    /// <param name="requisitionNo">The requisition.</param>
    /// <param name="reason">Why.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisition, or the reason it could not be cancelled.</returns>
    public async Task<Result<PurchaseRequisition>> CancelAsync(
        string requisitionNo,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var requisition = await LoadAsync(requisitionNo, cancellationToken).ConfigureAwait(false);

        if (requisition is null)
        {
            return NotFound(requisitionNo);
        }

        if (requisition.Lines.Any(static l => l.QuantityOrdered > 0m))
        {
            // Orders have already been raised from it. Cancelling now would leave those orders
            // pointing at a document that says nothing was ever wanted.
            return Result<PurchaseRequisition>.Failure(messages.Render(
                PurchasingMessages.RequisitionAlreadyOrdered,
                Args(("RequisitionNo", requisition.No))));
        }

        requisition.Status = PurchaseRequisitionStatus.Cancelled;
        requisition.RejectionReason = reason;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseRequisition>.Success(requisition);
    }

    /// <summary>Loads a requisition and its lines.</summary>
    /// <param name="requisitionNo">The number.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisition, or null when nothing carries that number.</returns>
    public Task<PurchaseRequisition?> LoadAsync(
        string requisitionNo,
        CancellationToken cancellationToken = default)
        => context.Set<PurchaseRequisition>()
            .Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.No == requisitionNo, cancellationToken);

    /// <summary>The requisitions, newest first.</summary>
    /// <param name="status">One status, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The requisitions.</returns>
    public async Task<IReadOnlyList<PurchaseRequisition>> ListAsync(
        PurchaseRequisitionStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = context.Set<PurchaseRequisition>().AsNoTracking().Include(r => r.Lines).AsQueryable();

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
    /// <param name="requisitionNo">The number that was asked for.</param>
    /// <returns>The failure.</returns>
    public Result<PurchaseRequisition> NotFound(string requisitionNo)
        => Result<PurchaseRequisition>.Failure(messages.Render(
            PurchasingMessages.RequisitionNotFound,
            Args(("RequisitionNo", requisitionNo))));

    /// <summary>What is going onto the order: what the caller said, or everything left.</summary>
    private static List<(PurchaseRequisitionLine Line, decimal Quantity, decimal UnitCost)> Going(
        PurchaseRequisition requisition,
        IReadOnlyList<RequisitionOrderLineRequest>? lines)
    {
        if (lines is null)
        {
            return
            [
                .. requisition.Lines
                    .Where(static l => l.OutstandingToOrder > 0m)
                    .OrderBy(static l => l.LineNo)
                    .Select(static l => (l, l.OutstandingToOrder, l.EstimatedUnitCost)),
            ];
        }

        var byLineNo = requisition.Lines.ToDictionary(static l => l.LineNo);

        return
        [
            .. lines
                .Where(r => r.Quantity > 0m && byLineNo.ContainsKey(r.LineNo))
                .Select(r => (byLineNo[r.LineNo], r.Quantity, r.DirectUnitCost)),
        ];
    }

    /// <summary>
    /// Says so when more is being ordered than was ever asked for.
    /// </summary>
    /// <remarks>
    /// Not overridable. A requisition is an authority for a quantity, and ordering past it means
    /// the authority covers something nobody signed for -- which is the one thing the whole
    /// approval exercise exists to prevent.
    /// </remarks>
    private List<AsapMessage> Check(
        PurchaseRequisition requisition,
        List<(PurchaseRequisitionLine Line, decimal Quantity, decimal UnitCost)> going)
    {
        var found = new List<AsapMessage>();

        foreach (var (line, quantity, _) in going.Where(g => g.Quantity > g.Line.OutstandingToOrder))
        {
            found.Add(messages.Render(
                PurchasingMessages.OrderExceedsRequisition,
                Args(
                    ("RequisitionNo", requisition.No),
                    ("LineNo", line.LineNo),
                    ("ItemNo", line.ItemNo ?? line.AccountNo),
                    ("Ordered", quantity),
                    ("OutstandingQuantity", line.OutstandingToOrder),
                    ("Requested", line.Quantity)),
                MessageTarget.OnField($"Lines[{line.LineNo}]")));
        }

        return found;
    }

    private async Task<string> SeriesCodeAsync(CancellationToken cancellationToken)
        => await setup
               .GetAsync<string>($"{PurchasingModule.Id}.Requisitions.NumberSeries", cancellationToken)
               .ConfigureAwait(false)
           ?? "PURCH-REQ";

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
