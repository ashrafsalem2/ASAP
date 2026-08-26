using ASAP.Modules.Inventory.Ledger;
using ASAP.Modules.Inventory.Posting;
using Shouldly;

namespace ASAP.Modules.Inventory.Tests.Posting;

/// <summary>
/// Covers which accounts a stock movement touches.
///
/// The rules are ordinary double entry, but getting one of them backwards produces books that
/// balance perfectly and describe something that never happened -- which is far harder to notice
/// than an error that refuses to post.
/// </summary>
public sealed class InventoryAccountsTests
{
    private static InventoryAccounts.CategoryAccounts Accounts(
        string? inventory = "1400",
        string? cogs = "5100",
        string? variance = "5300")
        => new(inventory, cogs, variance);

    [Fact]
    public void A_sale_moves_value_from_stock_to_cost_of_goods_sold()
    {
        // Goods sold stop being an asset and become a cost. Inventory falls by what they were
        // worth and cost of goods sold rises by the same figure.
        var lines = InventoryAccounts.ForMovement(
            ItemLedgerEntryType.Sale,
            costAmount: -135.00m,
            Accounts(),
            "Sale of widgets");

        lines.Count.ShouldBe(2);

        lines.ShouldContain(l => l.AccountNo == "1400" && l.Amount == -135.00m);
        lines.ShouldContain(l => l.AccountNo == "5100" && l.Amount == 135.00m);
    }

    [Fact]
    public void A_purchase_raises_stock_against_the_variance_account_until_purchasing_exists()
    {
        // The vendor side belongs to Purchasing, which owns that half of the transaction. Until
        // the module is there the balance sits in variance, so the books stay square rather than
        // waiting for a module to arrive.
        var lines = InventoryAccounts.ForMovement(
            ItemLedgerEntryType.Purchase,
            costAmount: 135.00m,
            Accounts(),
            "Receipt of widgets");

        lines.ShouldContain(l => l.AccountNo == "1400" && l.Amount == 135.00m);
        lines.ShouldContain(l => l.AccountNo == "5300" && l.Amount == -135.00m);
    }

    [Fact]
    public void An_adjustment_has_no_counterparty_so_the_difference_lands_in_variance()
    {
        var lines = InventoryAccounts.ForMovement(
            ItemLedgerEntryType.NegativeAdjustment,
            costAmount: -40.00m,
            Accounts(),
            "Breakage");

        lines.ShouldContain(l => l.AccountNo == "1400" && l.Amount == -40.00m);
        lines.ShouldContain(l => l.AccountNo == "5300" && l.Amount == 40.00m);
    }

    [Fact]
    public void Every_movement_produces_lines_that_sum_to_zero()
    {
        // The ledger refuses anything unbalanced, so getting this wrong is caught rather than
        // absorbed -- but catching it here says which rule was wrong rather than only that one was.
        foreach (var entryType in Enum.GetValues<ItemLedgerEntryType>())
        {
            var lines = InventoryAccounts.ForMovement(entryType, 99.99m, Accounts(), "Test");

            // Some movements deliberately post nothing. What must never happen is a movement that
            // posts something which does not balance.
            lines.Sum(static l => l.Amount).ShouldBe(0m, $"{entryType} does not balance");
        }
    }

    [Fact]
    public void A_movement_worth_nothing_produces_no_entries()
    {
        // A zero-value posting balances perfectly and tells a reader nothing, which is a poor
        // trade for two more rows on every account.
        InventoryAccounts.ForMovement(ItemLedgerEntryType.Sale, 0m, Accounts(), "Nothing")
            .ShouldBeEmpty();
    }

    [Fact]
    public void An_item_whose_category_has_no_accounts_produces_no_entries()
    {
        // Stock still moves and is still valued; the value simply waits for someone to say where
        // it belongs. Refusing the movement would stop a shop trading over a setup step nobody
        // has reached yet.
        InventoryAccounts
            .ForMovement(ItemLedgerEntryType.Sale, -135.00m, Accounts(inventory: null), "Sale")
            .ShouldBeEmpty();
    }

    [Fact]
    public void A_settlement_corrects_cost_of_goods_sold_rather_than_variance()
    {
        // The goods were sold. The only thing that was wrong is how much they cost, so the figure
        // that needs fixing is the cost of the sale -- not an adjustment account.
        var lines = InventoryAccounts.ForSettlement(-15.00m, Accounts(), "Cost settled");

        lines.ShouldContain(l => l.AccountNo == "1400" && l.Amount == -15.00m);
        lines.ShouldContain(l => l.AccountNo == "5100" && l.Amount == 15.00m);
        lines.Sum(static l => l.Amount).ShouldBe(0m);
    }

    [Fact]
    public void A_settlement_of_nothing_posts_nothing()
    {
        InventoryAccounts.ForSettlement(0m, Accounts(), "Estimate was right").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(ItemLedgerEntryType.TransferOut)]
    [InlineData(ItemLedgerEntryType.TransferIn)]
    public void A_transfer_posts_nothing_at_all(ItemLedgerEntryType entryType)
    {
        // Moving goods between locations changes where they are, not what the company owns.
        // Booking it as inventory against inventory would balance perfectly and add two rows to
        // the account saying that nothing happened.
        InventoryAccounts.ForMovement(entryType, -50.00m, Accounts(), "Transfer to Jeddah")
            .ShouldBeEmpty();
    }
}
