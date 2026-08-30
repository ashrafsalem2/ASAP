using ASAP.Modules.Inventory.Ledger;
using ASAP.Platform.Kernel.Accounting;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Inventory.Items;

/// <summary>A category as somebody sets it up.</summary>
/// <param name="Code">Its code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">What it is called in Arabic.</param>
/// <param name="ParentCode">The category it sits under, for a hierarchy.</param>
/// <param name="InventoryAccountNo">Where the value of its stock is held.</param>
/// <param name="CostOfGoodsSoldAccountNo">Where the cost of what it sells is charged.</param>
/// <param name="SalesAccountNo">Where revenue from it is credited.</param>
/// <param name="VarianceAccountNo">Where an adjustment or a settled estimate lands.</param>
public readonly record struct ItemCategoryRequest(
    string Code,
    string Name,
    string? NameArabic = null,
    string? ParentCode = null,
    string? InventoryAccountNo = null,
    string? CostOfGoodsSoldAccountNo = null,
    string? SalesAccountNo = null,
    string? VarianceAccountNo = null);

/// <summary>What a category is not posting, and what that has cost so far.</summary>
/// <param name="Code">The category.</param>
/// <param name="Name">What it is called.</param>
/// <param name="NameArabic">The same in Arabic.</param>
/// <param name="ItemCount">How many items sit under it.</param>
/// <param name="MissingAccounts">Which of its four accounts have not been set.</param>
/// <param name="UnpostedValue">
/// The value of movements made under it that could not reach the ledger.
/// </param>
/// <param name="UnpostedEntryCount">How many movements that was.</param>
public readonly record struct CategoryPostingGap(
    string Code,
    string Name,
    string? NameArabic,
    int ItemCount,
    IReadOnlyList<string> MissingAccounts,
    decimal UnpostedValue,
    int UnpostedEntryCount);

/// <summary>
/// The categories items are grouped under, and the accounts each posts to.
/// </summary>
/// <remarks>
/// <para>
/// Accounts live on the category rather than the item, so a company with twelve thousand items
/// maintains six sets of accounts rather than twelve thousand. That is the reason the grouping
/// exists at all; naming things is a side effect.
/// </para>
/// <para>
/// Which makes an unset account the quiet failure this whole class is arranged around. A movement
/// under a category with no inventory account posts no ledger lines, on purpose -- refusing it
/// would stop a shop trading over a setup step nobody has reached. But the value still moved, and
/// nothing said so. <see cref="GapsAsync"/> is the thing that says so, and it reports what has
/// already gone unposted rather than only what will.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
/// <param name="tenancy">Says which company this is.</param>
/// <param name="chart">
/// Reads the chart of accounts, where a module owns one. Null on an installation with no general
/// ledger, and then account numbers are taken as given rather than refused.
/// </param>
public sealed class ItemCategoryService(
    AsapDbContext context,
    IMessageCatalog messages,
    ITenantContext tenancy,
    IChartOfAccounts? chart = null)
{
    /// <summary>The four accounts a category can name, in the order a screen shows them.</summary>
    private static readonly string[] AccountFields =
        ["InventoryAccountNo", "CostOfGoodsSoldAccountNo", "SalesAccountNo", "VarianceAccountNo"];

    /// <summary>
    /// The categories, in code order.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Every category.</returns>
    public async Task<IReadOnlyList<ItemCategory>> CategoriesAsync(
        CancellationToken cancellationToken = default)
        => await context.Set<ItemCategory>()
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Adds a category, or changes one already there.
    /// </summary>
    /// <param name="request">The category.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The category as saved, or why it was refused.</returns>
    public async Task<Result<ItemCategory>> SaveAsync(
        ItemCategoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            return Result<ItemCategory>.Failure(
                messages.Render(InventoryMessages.CategoryCodeRequired, Args()));
        }

        var refusals = new List<AsapMessage>();

        foreach (var (field, accountNo) in Named(request))
        {
            var refusal = await CheckAccountAsync(field, accountNo, cancellationToken).ConfigureAwait(false);

            if (refusal is not null)
            {
                refusals.Add(refusal);
            }
        }

        if (refusals.Count > 0)
        {
            return Result<ItemCategory>.Failure(refusals);
        }

        var existing = await context.Set<ItemCategory>()
            .FirstOrDefaultAsync(c => c.Code == code, cancellationToken)
            .ConfigureAwait(false);

        Guid? parentId = null;

        if (!string.IsNullOrWhiteSpace(request.ParentCode))
        {
            var parentCode = request.ParentCode.Trim().ToUpperInvariant();

            if (string.Equals(parentCode, code, StringComparison.OrdinalIgnoreCase))
            {
                return Result<ItemCategory>.Failure(messages.Render(
                    InventoryMessages.CategoryIsItsOwnParent,
                    Args(("Code", code))));
            }

            var parent = await context.Set<ItemCategory>()
                .FirstOrDefaultAsync(c => c.Code == parentCode, cancellationToken)
                .ConfigureAwait(false);

            if (parent is null)
            {
                return Result<ItemCategory>.Failure(messages.Render(
                    InventoryMessages.CategoryNotFound,
                    Args(("Code", parentCode))));
            }

            // A cycle would make anything that walks the tree run for ever, and the walk is the
            // point of having a parent at all.
            if (existing is not null
                && await DescendsFromAsync(parent, existing.Id, cancellationToken).ConfigureAwait(false))
            {
                return Result<ItemCategory>.Failure(messages.Render(
                    InventoryMessages.CategoryWouldLoop,
                    Args(("Code", code), ("ParentCode", parentCode))));
            }

            parentId = parent.Id;
        }

        if (existing is null)
        {
            existing = new ItemCategory
            {
                TenantId = tenancy.RequireTenantId(),
                CompanyId = tenancy.RequireCompanyId(),
                Code = code,
                Name = request.Name?.Trim() ?? code,
                NameArabic = request.NameArabic?.Trim(),
                ParentId = parentId,
                InventoryAccountNo = Blank(request.InventoryAccountNo),
                CostOfGoodsSoldAccountNo = Blank(request.CostOfGoodsSoldAccountNo),
                SalesAccountNo = Blank(request.SalesAccountNo),
                VarianceAccountNo = Blank(request.VarianceAccountNo),
            };

            context.Set<ItemCategory>().Add(existing);
        }
        else
        {
            existing.Name = request.Name?.Trim() ?? existing.Name;
            existing.NameArabic = request.NameArabic?.Trim();
            existing.ParentId = parentId;
            existing.InventoryAccountNo = Blank(request.InventoryAccountNo);
            existing.CostOfGoodsSoldAccountNo = Blank(request.CostOfGoodsSoldAccountNo);
            existing.SalesAccountNo = Blank(request.SalesAccountNo);
            existing.VarianceAccountNo = Blank(request.VarianceAccountNo);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<ItemCategory>.Success(existing);

        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Moves an item into a category.
    /// </summary>
    /// <param name="itemNo">The item.</param>
    /// <param name="categoryCode">The category, or null to take it out of any.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The item as saved, or why it was refused.</returns>
    /// <remarks>
    /// Movements already posted keep the accounts they posted to. A category is where an item's
    /// accounts are read from at the moment of posting, not a claim about where its history went,
    /// and restating a closed month by regrouping a catalogue would be a surprising thing for a
    /// dropdown to do.
    /// </remarks>
    public async Task<Result<Item>> SetCategoryAsync(
        string itemNo,
        string? categoryCode,
        CancellationToken cancellationToken = default)
    {
        var normalised = itemNo?.Trim().ToUpperInvariant() ?? string.Empty;

        var item = await context.Set<Item>()
            .FirstOrDefaultAsync(i => i.No == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (item is null)
        {
            return Result<Item>.Failure(messages.Render(
                InventoryMessages.ItemNotFound,
                Args(("ItemNo", normalised))));
        }

        if (string.IsNullOrWhiteSpace(categoryCode))
        {
            item.CategoryId = null;

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Result<Item>.Success(item);
        }

        var code = categoryCode.Trim().ToUpperInvariant();

        var category = await context.Set<ItemCategory>()
            .FirstOrDefaultAsync(c => c.Code == code, cancellationToken)
            .ConfigureAwait(false);

        if (category is null)
        {
            return Result<Item>.Failure(messages.Render(
                InventoryMessages.CategoryNotFound,
                Args(("Code", code))));
        }

        item.CategoryId = category.Id;

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Item>.Success(item);
    }

    /// <summary>
    /// Which categories will not reach the general ledger, and what that has cost so far.
    /// </summary>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>A row per category with something missing, worst first.</returns>
    /// <remarks>
    /// <para>
    /// The answer to the one thing about this design that is genuinely dangerous. A movement under
    /// a category with no inventory account posts no ledger lines at all, deliberately, so a shop
    /// is not stopped from trading over a setup step nobody has reached. The cost is that a company
    /// can run for months with its inventory account frozen and nothing ever says so.
    /// </para>
    /// <para>
    /// So this reports the value that has already gone unposted, not only the setup that is
    /// missing. A screen saying "four accounts are blank" is a chore; one saying "and 84,000 riyals
    /// of stock movement never reached the ledger because of it" is a decision.
    /// </para>
    /// <para>
    /// Items with no category at all are gathered under a row of their own, because they have the
    /// same problem for a different reason and leaving them out would understate the figure by
    /// exactly the items nobody has classified.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<CategoryPostingGap>> GapsAsync(
        CancellationToken cancellationToken = default)
    {
        var categories = await context.Set<ItemCategory>()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = await context.Set<Item>()
            .AsNoTracking()
            .Select(static i => new { i.Id, i.CategoryId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Value entries carry what actually moved. An entry whose category could not name an
        // inventory account is one the ledger never heard about, whatever the item ledger says.
        var valueByItem = await context.Set<ValueEntry>()
            .Where(static v => !v.IsExpected)
            .GroupBy(static v => v.ItemId)
            .Select(static g => new
            {
                ItemId = g.Key,
                Cost = g.Sum(static v => v.CostAmount),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var costByItem = valueByItem.ToDictionary(static v => v.ItemId, static v => (v.Cost, v.Count));

        var gaps = new List<CategoryPostingGap>();

        foreach (var category in categories)
        {
            var missing = MissingOn(category);

            if (missing.Count == 0)
            {
                continue;
            }

            var members = items.Where(i => i.CategoryId == category.Id).ToList();

            var (cost, count) = Totals(members.Select(static m => m.Id), costByItem);

            gaps.Add(new CategoryPostingGap(
                category.Code,
                category.Name,
                category.NameArabic,
                members.Count,
                missing,
                cost,
                count));
        }

        var orphans = items.Where(static i => i.CategoryId is null).ToList();

        if (orphans.Count > 0)
        {
            var (cost, count) = Totals(orphans.Select(static o => o.Id), costByItem);

            gaps.Add(new CategoryPostingGap(
                string.Empty,
                "Items in no category",
                "أصناف بلا فئة",
                orphans.Count,
                AccountFields,
                cost,
                count));
        }

        return [.. gaps.OrderByDescending(static g => Math.Abs(g.UnpostedValue)).ThenBy(static g => g.Code, StringComparer.OrdinalIgnoreCase)];

        static (decimal Cost, int Count) Totals(
            IEnumerable<Guid> itemIds,
            Dictionary<Guid, (decimal Cost, int Count)> costs)
        {
            var cost = 0m;
            var count = 0;

            foreach (var id in itemIds)
            {
                if (costs.TryGetValue(id, out var found))
                {
                    cost += found.Cost;
                    count += found.Count;
                }
            }

            return (cost, count);
        }
    }

    /// <summary>Which of a category's four accounts have not been set.</summary>
    /// <remarks>
    /// The inventory account is the one that stops everything: without it no line is built at all,
    /// whatever the other three say. The rest are reported alongside because a company setting one
    /// up wants the whole list, not one field at a time.
    /// </remarks>
    private static List<string> MissingOn(ItemCategory category)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(category.InventoryAccountNo))
        {
            missing.Add("InventoryAccountNo");
        }

        if (string.IsNullOrWhiteSpace(category.CostOfGoodsSoldAccountNo))
        {
            missing.Add("CostOfGoodsSoldAccountNo");
        }

        if (string.IsNullOrWhiteSpace(category.SalesAccountNo))
        {
            missing.Add("SalesAccountNo");
        }

        if (string.IsNullOrWhiteSpace(category.VarianceAccountNo))
        {
            missing.Add("VarianceAccountNo");
        }

        return missing;
    }

    private static IEnumerable<(string Field, string AccountNo)> Named(ItemCategoryRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.InventoryAccountNo))
        {
            yield return ("InventoryAccountNo", request.InventoryAccountNo.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.CostOfGoodsSoldAccountNo))
        {
            yield return ("CostOfGoodsSoldAccountNo", request.CostOfGoodsSoldAccountNo.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.SalesAccountNo))
        {
            yield return ("SalesAccountNo", request.SalesAccountNo.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.VarianceAccountNo))
        {
            yield return ("VarianceAccountNo", request.VarianceAccountNo.Trim());
        }
    }

    /// <summary>
    /// Says why an account will not do, when it will not.
    /// </summary>
    /// <remarks>
    /// Nothing is checked where no module owns a chart of accounts. A company running stock without
    /// a general ledger is a supported way to run, and turning an unanswerable question into a
    /// refusal would make it not one.
    /// </remarks>
    private async Task<AsapMessage?> CheckAccountAsync(
        string field,
        string accountNo,
        CancellationToken cancellationToken)
    {
        if (chart is null)
        {
            return null;
        }

        var described = await chart.DescribeAsync(accountNo, cancellationToken).ConfigureAwait(false);

        if (described is null)
        {
            return messages.Render(
                InventoryMessages.CategoryAccountNotFound,
                Args(("AccountNo", accountNo), ("Field", field)),
                MessageTarget.OnField(field));
        }

        if (described.Value.Postability is AccountPostability.Postable)
        {
            return null;
        }

        // Two refusals rather than one carrying the reason as an argument. The reason would be an
        // enum name in English sitting inside an Arabic sentence, and the two have different
        // answers anyway: a blocked account can be unblocked, a heading never becomes postable.
        var code = described.Value.Postability is AccountPostability.Blocked
            ? InventoryMessages.CategoryAccountBlocked
            : InventoryMessages.CategoryAccountIsNotForPosting;

        return messages.Render(
            code,
            Args(
                ("AccountNo", accountNo),
                ("AccountName", described.Value.Name),
                ("Field", field)),
            MessageTarget.OnField(field));
    }

    private async Task<bool> DescendsFromAsync(
        ItemCategory candidate,
        Guid ancestorId,
        CancellationToken cancellationToken)
    {
        var seen = new HashSet<Guid>();
        var walker = candidate;

        while (walker is not null && seen.Add(walker.Id))
        {
            if (walker.Id == ancestorId)
            {
                return true;
            }

            if (walker.ParentId is not { } parentId)
            {
                return false;
            }

            walker = await context.Set<ItemCategory>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parentId, cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
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
