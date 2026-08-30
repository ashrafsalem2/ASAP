using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Adjustments;

/// <summary>A reason as somebody sets it up.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="ContraAccountNo">Where the value lands, or null for the category's variance account.</param>
/// <param name="Direction">Which way it may move stock.</param>
/// <param name="RequiresNote">Whether the person adjusting has to say something as well.</param>
/// <param name="IsActive">Whether it may still be chosen.</param>
public readonly record struct AdjustmentReasonRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string? ContraAccountNo = null,
    AdjustmentDirection Direction = AdjustmentDirection.Either,
    bool RequiresNote = false,
    bool IsActive = true);

/// <summary>What was adjusted under one reason.</summary>
/// <param name="ReasonCode">The reason.</param>
/// <param name="ReasonName">What it is called.</param>
/// <param name="ReasonNameArabic">The same in Arabic.</param>
/// <param name="EntryCount">How many adjustments carried it.</param>
/// <param name="Quantity">The net quantity moved under it.</param>
/// <param name="CostAmount">What that was worth.</param>
public readonly record struct ShrinkageRow(
    string ReasonCode,
    string ReasonName,
    string? ReasonNameArabic,
    int EntryCount,
    decimal Quantity,
    decimal CostAmount);

/// <summary>
/// The reasons a company adjusts stock for, and what was adjusted under each.
/// </summary>
/// <remarks>
/// The report at the bottom is why the list exists. Breakage, theft and expiry have the same
/// effect on quantity and almost nothing else in common: one is a warehouse conversation, one is a
/// security one, one is a buying one. A single shrinkage figure covering all three answers none of
/// them.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
public sealed class AdjustmentReasonService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy)
{
    /// <summary>
    /// The reasons, in code order.
    /// </summary>
    /// <param name="includeWithdrawn">Whether to list the ones no longer in use.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The reasons.</returns>
    public async Task<IReadOnlyList<AdjustmentReason>> ReasonsAsync(
        bool includeWithdrawn = false,
        CancellationToken cancellationToken = default)
        => await context.Set<AdjustmentReason>()
            .AsNoTracking()
            .Where(r => includeWithdrawn || r.IsActive)
            .OrderBy(r => r.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Adds a reason, or changes one already there.
    /// </summary>
    /// <param name="request">The reason.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The reason as saved, or why it was refused.</returns>
    public async Task<Result<AdjustmentReason>> SaveAsync(
        AdjustmentReasonRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<AdjustmentReason>.Failure(
                messages.Render(InventoryMessages.ReasonCodeRequired, Args()));
        }

        var existing = await context.Set<AdjustmentReason>()
            .FirstOrDefaultAsync(r => r.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            existing = new AdjustmentReason
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                Code = code,
                Name = request.Name?.Trim() ?? code,
                NameArabic = request.NameArabic?.Trim(),
                ContraAccountNo = Blank(request.ContraAccountNo),
                Direction = request.Direction,
                RequiresNote = request.RequiresNote,
                IsActive = request.IsActive,
            };

            context.Set<AdjustmentReason>().Add(existing);
        }
        else
        {
            existing.Name = request.Name?.Trim() ?? existing.Name;
            existing.NameArabic = request.NameArabic?.Trim();
            existing.ContraAccountNo = Blank(request.ContraAccountNo);
            existing.Direction = request.Direction;
            existing.RequiresNote = request.RequiresNote;
            existing.IsActive = request.IsActive;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<AdjustmentReason>.Success(existing);

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// What was adjusted under each reason over a period.
    /// </summary>
    /// <param name="from">The first day counted.</param>
    /// <param name="to">The last day counted.</param>
    /// <param name="locationCode">One location, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A row per reason, biggest loss first.</returns>
    /// <remarks>
    /// <para>
    /// Adjustments with no reason on them are gathered under a row of their own rather than
    /// dropped. A report that quietly omitted them would understate the total, and the gap between
    /// what it showed and what the ledger said would be exactly the entries nobody explained --
    /// which is the last thing a shrinkage report should hide.
    /// </para>
    /// <para>
    /// The value comes from the value entries rather than from an item's current cost, because
    /// what a write-off cost is what the goods were worth when they were written off.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<ShrinkageRow>> ShrinkageAsync(
        DateOnly from,
        DateOnly to,
        string? locationCode = null,
        CancellationToken cancellationToken = default)
    {
        var location = locationCode?.Trim().ToUpperInvariant();

        var totals = await context.Set<ItemLedgerEntry>()
            .Where(e => e.PostingDate >= from && e.PostingDate <= to
                && (e.EntryType == ItemLedgerEntryType.PositiveAdjustment
                    || e.EntryType == ItemLedgerEntryType.NegativeAdjustment)
                && (location == null || e.LocationCode == location))
            .GroupBy(static e => e.ReasonCode)
            .Select(static g => new
            {
                ReasonCode = g.Key,
                EntryCount = g.Count(),
                Quantity = g.Sum(static e => e.Quantity),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (totals.Count == 0)
        {
            return [];
        }

        var costs = await context.Set<ValueEntry>()
            .Where(v => v.PostingDate >= from && v.PostingDate <= to
                && (v.ItemLedgerEntryType == ItemLedgerEntryType.PositiveAdjustment
                    || v.ItemLedgerEntryType == ItemLedgerEntryType.NegativeAdjustment)
                && v.ItemLedgerEntry != null
                && (location == null || v.ItemLedgerEntry.LocationCode == location))
            .GroupBy(static v => v.ItemLedgerEntry!.ReasonCode)
            .Select(static g => new { ReasonCode = g.Key, Cost = g.Sum(static v => v.CostAmount) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var costByReason = costs.ToDictionary(
            static c => c.ReasonCode ?? string.Empty,
            static c => c.Cost,
            StringComparer.OrdinalIgnoreCase);

        var named = await context.Set<AdjustmentReason>()
            .AsNoTracking()
            .ToDictionaryAsync(static r => r.Code, StringComparer.OrdinalIgnoreCase, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. totals
                .Select(t =>
                {
                    var code = t.ReasonCode ?? string.Empty;
                    var reason = code.Length == 0 ? null : named.GetValueOrDefault(code);

                    return new ShrinkageRow(
                        code,
                        reason?.Name ?? (code.Length == 0 ? "No reason given" : code),
                        reason?.NameArabic ?? (code.Length == 0 ? "بلا سبب" : null),
                        t.EntryCount,
                        t.Quantity,
                        costByReason.GetValueOrDefault(code));
                })
                .OrderBy(static r => r.CostAmount)
                .ThenBy(static r => r.ReasonCode, StringComparer.OrdinalIgnoreCase),
        ];
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
