using ASAP.Modules.Purchasing.Orders;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Setup;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ASAP.Modules.Purchasing.Approvals;

/// <summary>An approval limit as somebody sets it up.</summary>
/// <param name="UserId">The person.</param>
/// <param name="UserName">Their user name.</param>
/// <param name="DisplayName">What they are called.</param>
/// <param name="MaximumAmount">The most they may approve, on one order.</param>
/// <param name="IsActive">Whether the limit is still in force.</param>
public readonly record struct ApprovalLimitRequest(
    Guid UserId,
    string UserName,
    string? DisplayName,
    decimal MaximumAmount,
    bool IsActive = true);

/// <summary>
/// Decides whether a purchase order may go to the vendor, and who may say so.
/// </summary>
/// <remarks>
/// <para>
/// Two rules, and the second is the one that makes the first mean anything.
/// </para>
/// <para>
/// An order over the company's threshold needs somebody whose limit covers it. And that somebody
/// is never the person who raised it. An approval you can give yourself is not a control, it is a
/// checkbox: the whole point is that a second person looked. Segregation of duties is the reason
/// the feature exists, and a system that lets a buyer sign their own order has the paperwork of an
/// approval process and none of its substance.
/// </para>
/// <para>
/// A limit is per person and per order. Somebody with no limit at all approves nothing, because a
/// system where unknown means unlimited is one where the answer to "who can approve this" is
/// "whoever has not been set up yet".
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="setup">Reads the company's threshold.</param>
/// <param name="user">Says who is asking.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="clock">Supplies the time an approval was given.</param>
/// <param name="logger">Records approvals.</param>
public sealed class PurchaseApprovalService(
    AsapDbContext context,
    IMessageCatalog messages,
    ISetupService setup,
    IUserContext user,
    ITenantContext tenancy,
    IClock clock,
    ILogger<PurchaseApprovalService> logger)
{
    /// <summary>The setting that says what may go out without a signature.</summary>
    public const string ThresholdKey = "Purchasing.Approval.Threshold";

    /// <summary>
    /// The limits in force, most authority first.
    /// </summary>
    /// <param name="includeWithdrawn">Whether to list the ones no longer in force.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The limits.</returns>
    public async Task<IReadOnlyList<PurchaseApprovalLimit>> LimitsAsync(
        bool includeWithdrawn = false,
        CancellationToken cancellationToken = default)
        => await context.Set<PurchaseApprovalLimit>()
            .AsNoTracking()
            .Where(l => includeWithdrawn || l.IsActive)
            .OrderByDescending(l => l.MaximumAmount)
            .ThenBy(l => l.UserName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Sets what one person may approve.
    /// </summary>
    /// <param name="request">The limit.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The limit as saved, or why it was refused.</returns>
    public async Task<Result<PurchaseApprovalLimit>> SetLimitAsync(
        ApprovalLimitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.MaximumAmount < 0m)
        {
            return Result<PurchaseApprovalLimit>.Failure(messages.Render(
                PurchasingMessages.ApprovalLimitNegative,
                Args(("Amount", request.MaximumAmount))));
        }

        var existing = await context.Set<PurchaseApprovalLimit>()
            .FirstOrDefaultAsync(l => l.UserId == request.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new PurchaseApprovalLimit
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                UserId = request.UserId,
                UserName = request.UserName?.Trim() ?? string.Empty,
                DisplayName = request.DisplayName?.Trim(),
                MaximumAmount = request.MaximumAmount,
                IsActive = request.IsActive,
            };

            context.Set<PurchaseApprovalLimit>().Add(existing);
        }
        else
        {
            existing.UserName = request.UserName?.Trim() ?? existing.UserName;
            existing.DisplayName = request.DisplayName?.Trim();
            existing.MaximumAmount = request.MaximumAmount;
            existing.IsActive = request.IsActive;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PurchaseApprovalLimit>.Success(existing);
    }

    /// <summary>
    /// Whether an order of this size has to be signed for.
    /// </summary>
    /// <param name="amount">What the order comes to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>True when it needs an approval.</returns>
    /// <remarks>
    /// A threshold of nought means everything is signed for, which is a coherent choice for a
    /// company that wants it. There is no way to say "nothing ever needs approval" other than
    /// setting the threshold high, and that is deliberate: it leaves the decision visible as a
    /// number somebody chose rather than as a feature nobody switched on.
    /// </remarks>
    public async Task<bool> NeedsApprovalAsync(decimal amount, CancellationToken cancellationToken = default)
    {
        var threshold = await setup.GetAsync<decimal>(ThresholdKey, cancellationToken).ConfigureAwait(false);

        return amount > threshold;
    }

    /// <summary>
    /// Signs for an order.
    /// </summary>
    /// <param name="order">The order, with its lines loaded.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether it was approved, or why not.</returns>
    public async Task<Result> ApproveAsync(PurchaseOrder order, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Status is not PurchaseOrderStatus.PendingApproval)
        {
            return Result.Failure(messages.Render(
                PurchasingMessages.OrderNotAwaitingApproval,
                Args(("OrderNo", order.No), ("Status", order.Status.ToString()))));
        }

        var approverId = user.RequireUserId();

        // The rule the whole feature turns on. An approval you can give yourself is not a control,
        // it is a checkbox, and the point of the exercise is that a second person looked.
        if (order.RaisedByUserId == approverId)
        {
            return Result.Failure(messages.Render(
                PurchasingMessages.CannotApproveYourOwnOrder,
                Args(("OrderNo", order.No))));
        }

        var total = order.TotalAmount;

        var limit = await context.Set<PurchaseApprovalLimit>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.UserId == approverId && l.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (limit is null || limit.MaximumAmount < total)
        {
            // Naming somebody who can is the difference between a refusal and a dead end. The
            // person holding the order has to know where to take it next.
            var whoCan = await context.Set<PurchaseApprovalLimit>()
                .AsNoTracking()
                .Where(l => l.IsActive && l.MaximumAmount >= total && l.UserId != order.RaisedByUserId)
                .OrderBy(l => l.MaximumAmount)
                .Select(static l => l.DisplayName ?? l.UserName)
                .Take(3)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return Result.Failure(messages.Render(
                PurchasingMessages.ApprovalLimitTooLow,
                Args(
                    ("OrderNo", order.No),
                    ("Amount", total),
                    ("Limit", limit?.MaximumAmount ?? 0m),
                    ("WhoCan", whoCan.Count > 0 ? string.Join(", ", whoCan) : string.Empty))));
        }

        order.Status = PurchaseOrderStatus.Released;
        order.ApprovedByUserId = approverId;
        order.ApprovedByUserName = user.UserName;
        order.ApprovedAtUtc = clock.UtcNow;

        // Frozen at the amount signed for, so anything that changes the total has to ask again.
        order.ApprovedAmount = total;
        order.RejectionReason = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "{OrderNo} approved for {Amount} by {Approver}.",
            order.No,
            total,
            user.UserName);

        return Result.Success(
        [
            messages.Render(
                PurchasingMessages.OrderApproved,
                Args(("OrderNo", order.No), ("Amount", total), ("Approver", user.UserName))),
        ]);
    }

    /// <summary>
    /// Turns an order down.
    /// </summary>
    /// <param name="order">The order.</param>
    /// <param name="reason">Why, which the person who raised it will read.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Whether it was rejected, or why not.</returns>
    /// <remarks>
    /// A reason is required. A rejection with nothing written against it sends the buyer back to
    /// guess at what was wrong, and the order comes round again unchanged.
    /// </remarks>
    public async Task<Result> RejectAsync(
        PurchaseOrder order,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Status is not PurchaseOrderStatus.PendingApproval)
        {
            return Result.Failure(messages.Render(
                PurchasingMessages.OrderNotAwaitingApproval,
                Args(("OrderNo", order.No), ("Status", order.Status.ToString()))));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(messages.Render(
                PurchasingMessages.RejectionNeedsAReason,
                Args(("OrderNo", order.No))));
        }

        order.Status = PurchaseOrderStatus.Rejected;
        order.RejectionReason = reason.Trim();
        order.ApprovedByUserId = null;
        order.ApprovedByUserName = null;
        order.ApprovedAtUtc = null;
        order.ApprovedAmount = null;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(
        [
            messages.Render(
                PurchasingMessages.OrderRejected,
                Args(("OrderNo", order.No), ("Reason", reason.Trim()))),
        ]);
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
