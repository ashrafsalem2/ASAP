using ASAP.Modules.Inventory.Items;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;

namespace ASAP.Modules.Inventory.Costing;

/// <summary>What the engine needs to know about an item to judge a movement.</summary>
/// <param name="ItemNo">The item number.</param>
/// <param name="Name">Its description.</param>
/// <param name="Kind">Whether it is stocked at all.</param>
/// <param name="CostingMethod">How it is costed.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
/// <param name="AllowNegativeInventory">
/// Whether this item in particular may go below zero, or null to follow the company.
/// </param>
/// <param name="UnitCost">Current cost per unit, used to value a shortfall.</param>
/// <param name="ReorderPoint">The level at which the item should be reordered.</param>
public sealed record ItemView(
    string ItemNo,
    string Name,
    ItemKind Kind,
    CostingMethod CostingMethod,
    bool IsBlocked,
    bool? AllowNegativeInventory,
    decimal UnitCost,
    decimal ReorderPoint);

/// <summary>What the engine needs to know about a location.</summary>
/// <param name="Code">The location code.</param>
/// <param name="Name">Its name.</param>
/// <param name="IsBlocked">Whether it is withdrawn from use.</param>
/// <param name="IsSellable">Whether stock here may be sold or shipped.</param>
public sealed record LocationView(string Code, string Name, bool IsBlocked, bool IsSellable);

/// <summary>One movement about to be posted.</summary>
/// <param name="LineNo">Position in the batch, so a message can point at the right row.</param>
/// <param name="Item">The item moving.</param>
/// <param name="Location">Where it is moving at.</param>
/// <param name="Quantity">Signed. Positive is stock coming in, negative going out.</param>
/// <param name="QuantityOnHand">What is on hand at that location before this movement.</param>
/// <param name="EntryType">
/// What caused the movement. Needed because some rules are about selling in particular rather than
/// about stock leaving: goods may not be sold out of a quarantine bay, but transferring them out
/// of one is exactly how they legitimately leave it.
/// </param>
public sealed record MovementView(
    int LineNo,
    ItemView Item,
    LocationView Location,
    decimal Quantity,
    decimal QuantityOnHand,
    Ledger.ItemLedgerEntryType EntryType = Ledger.ItemLedgerEntryType.Sale)
{
    /// <summary>The bin it moves at, where the location tracks them.</summary>
    public Locations.Bin? Bin { get; init; }

    /// <summary>
    /// What that bin holds of this item now.
    /// </summary>
    /// <remarks>
    /// Kept beside the location's figure rather than instead of it, because the two answer
    /// different questions and only one of them is about how much stock there is. A bin short of
    /// something the location has is a shelf in the wrong place, not a shortage.
    /// </remarks>
    public decimal BinQuantityOnHand { get; init; }

    /// <summary>
    /// The other bins at this location holding the item, worked out only when this one is short.
    /// </summary>
    /// <remarks>
    /// Carried rather than looked up during the check, because the check is pure arithmetic and
    /// the answer to "where is it then" is a query. Empty when the shelf is short and nothing
    /// else has it either, which is a different problem and gets a different answer.
    /// </remarks>
    public IReadOnlyList<string> BinsHoldingIt { get; init; } = [];
}

/// <summary>
/// Decides whether stock may move, and says what it means when it goes below zero.
/// </summary>
/// <remarks>
/// <para>
/// This is where "allow selling into negative, without corrupting the cost" is actually settled,
/// and it is two decisions rather than one.
/// </para>
/// <para>
/// The first is whether to permit it at all, which is a business choice and belongs to the
/// company: a shop that can see the goods on the shelf should not be stopped by paperwork that has
/// not caught up, while a warehouse running serialised equipment usually should be. The setting
/// can be narrowed per item, because the right answer genuinely differs between loose produce and
/// a numbered appliance.
/// </para>
/// <para>
/// The second is what happens to the cost once it is permitted, and that is not a choice at all.
/// The shortfall is valued at an estimate, the movement is marked as having gone negative, and a
/// warning says so in plain terms. That warning is not decoration: it is the record that the
/// figure is provisional, and it is what the settlement routine looks for when the goods finally
/// arrive.
/// </para>
/// </remarks>
/// <param name="messages">Renders the messages.</param>
public sealed class StockAvailability(IMessageCatalog messages)
{
    /// <summary>
    /// Checks a set of movements.
    /// </summary>
    /// <param name="movements">The movements about to be posted.</param>
    /// <param name="companyAllowsNegative">Whether the company permits stock below zero.</param>
    /// <param name="heldOverridePermissions">Override permissions the caller holds.</param>
    /// <returns>
    /// A failure carrying every reason the movement is refused, or a success carrying the warnings
    /// that go with it -- stock gone negative, an item below its reorder point.
    /// </returns>
    public Result Check(
        IReadOnlyList<MovementView> movements,
        bool companyAllowsNegative,
        IReadOnlySet<string>? heldOverridePermissions = null)
    {
        ArgumentNullException.ThrowIfNull(movements);

        var found = new List<AsapMessage>();

        foreach (var movement in movements)
        {
            CheckMovement(movement, companyAllowsNegative, heldOverridePermissions, found);
        }

        return found.Exists(static m => m.IsFailure)
            ? Result.Failure(found)
            : Result.Success(found);
    }

    private void CheckMovement(
        MovementView movement,
        bool companyAllowsNegative,
        IReadOnlySet<string>? held,
        List<AsapMessage> found)
    {
        var target = MessageTarget.OnField($"Lines[{movement.LineNo}]");

        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["LineNo"] = movement.LineNo,
            ["ItemNo"] = movement.Item.ItemNo,
            ["ItemName"] = movement.Item.Name,
            ["Location"] = movement.Location.Name,
        };

        if (movement.Quantity == 0)
        {
            found.Add(Raise(InventoryMessages.QuantityZero, arguments, target, held));
        }

        if (movement.Item.IsBlocked)
        {
            found.Add(Raise(InventoryMessages.ItemBlocked, arguments, target, held));
        }

        if (movement.Location.IsBlocked)
        {
            found.Add(Raise(InventoryMessages.LocationBlocked, arguments, target, held));
        }

        // A service or a charge has no stock, so nothing below here applies to it.
        if (movement.Item.Kind is not ItemKind.Inventory)
        {
            return;
        }

        if (movement.Quantity >= 0)
        {
            return;
        }

        // Stock at a quarantine or in-transit location is counted in the valuation but must not be
        // promised to a customer. The rule is about selling, not about stock leaving: a transfer
        // out of an in-transit location is how goods complete their journey, and refusing it would
        // strand every transfer half way.
        var isSale = movement.EntryType is Ledger.ItemLedgerEntryType.Sale
                     or Ledger.ItemLedgerEntryType.PurchaseReturn;

        if (isSale && !movement.Location.IsSellable)
        {
            found.Add(Raise(InventoryMessages.LocationNotSellable, arguments, target, held));
        }

        CheckBin(movement, arguments, target, found);

        var requested = -movement.Quantity;
        var balance = movement.QuantityOnHand + movement.Quantity;

        if (balance >= 0)
        {
            WarnIfBelowReorderPoint(movement, balance, arguments, target, found);
            return;
        }

        var shortfall = -balance;
        var allowed = movement.Item.AllowNegativeInventory ?? companyAllowsNegative;

        arguments["Requested"] = requested;
        arguments["AvailableQuantity"] = movement.QuantityOnHand;
        arguments["BalanceQuantity"] = balance;
        arguments["ShortfallQuantity"] = shortfall;
        arguments["EstimatedUnitCost"] = movement.Item.UnitCost;

        if (!allowed)
        {
            found.Add(Raise(InventoryMessages.NegativeInventoryBlocked, arguments, target, held));
            return;
        }

        // Permitted, and said out loud. The sale proceeds; this is the record that part of its
        // cost is an estimate, which is what the settlement routine will come back for.
        found.Add(Raise(InventoryMessages.NegativeInventoryAllowed, arguments, target, held));
    }

    /// <summary>
    /// Says when the shelf the goods were taken from has not got them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A warning rather than a refusal, and deliberately. The location still has the stock, so
    /// nothing about the valuation or the cost is in doubt -- what is wrong is the record of which
    /// shelf it is standing on, which is a put-away or a pick that went to the wrong place.
    /// Blocking the issue would stop a picker who is holding the goods in their hand.
    /// </para>
    /// <para>
    /// Two shapes, because they have different answers. Other bins hold it, and somebody should
    /// move the stock between bins to say where it really is; or nothing is in any bin, which is
    /// what stock received before the location started tracking bins looks like and wants a count
    /// onto the shelves once.
    /// </para>
    /// </remarks>
    private void CheckBin(
        MovementView movement,
        Dictionary<string, object?> arguments,
        MessageTarget target,
        List<AsapMessage> found)
    {
        if (movement.Bin is null || movement.Quantity >= 0m)
        {
            return;
        }

        var afterwards = movement.BinQuantityOnHand + movement.Quantity;

        if (afterwards >= 0m)
        {
            return;
        }

        var binArguments = new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase)
        {
            ["BinCode"] = movement.Bin.Code,
            ["BinQuantity"] = movement.BinQuantityOnHand,
            ["Requested"] = -movement.Quantity,
            ["LocationQuantity"] = movement.QuantityOnHand,
        };

        if (movement.BinsHoldingIt.Count == 0)
        {
            found.Add(messages.Render(InventoryMessages.BinStockNotPutAway, binArguments, target));
            return;
        }

        binArguments["Elsewhere"] = string.Join(", ", movement.BinsHoldingIt);

        found.Add(messages.Render(InventoryMessages.BinShortOfStock, binArguments, target));
    }

    private void WarnIfBelowReorderPoint(
        MovementView movement,
        decimal balance,
        Dictionary<string, object?> arguments,
        MessageTarget target,
        List<AsapMessage> found)
    {
        if (movement.Item.ReorderPoint <= 0 || balance > movement.Item.ReorderPoint)
        {
            return;
        }

        // Only when the movement takes it across the line. Warning on every subsequent sale of an
        // item already below its point would train people to ignore the warning entirely.
        if (movement.QuantityOnHand <= movement.Item.ReorderPoint)
        {
            return;
        }

        found.Add(messages.Render(
            InventoryMessages.BelowReorderPoint,
            new Dictionary<string, object?>(arguments, StringComparer.OrdinalIgnoreCase)
            {
                ["BalanceQuantity"] = balance,
                ["ReorderPoint"] = movement.Item.ReorderPoint,
            },
            target));
    }

    /// <summary>
    /// Renders a message, downgrading a block the caller is permitted to override.
    /// </summary>
    /// <remarks>
    /// The same rule the posting engine uses: holding the override permission turns a refusal into
    /// a warning, the operation proceeds, and the audit log records that someone pushed past a
    /// protection. The rule lives on the message definition, not in scattered conditions.
    /// </remarks>
    private AsapMessage Raise(
        MessageCode code,
        Dictionary<string, object?> arguments,
        MessageTarget target,
        IReadOnlySet<string>? held)
    {
        var rendered = messages.Render(code, arguments, target);

        return rendered.OverridePermission is { } permission && held?.Contains(permission) == true
            ? messages.AsOverridden(rendered)
            : rendered;
    }
}
