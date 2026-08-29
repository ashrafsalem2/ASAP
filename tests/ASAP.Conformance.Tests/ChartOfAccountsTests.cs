using ASAP.Modules.Finance.Accounts;
using ASAP.Modules.Finance.Seed;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Setup;
using Shouldly;

namespace ASAP.Conformance.Tests;

/// <summary>
/// Holds the shipped chart of accounts to the promises the setup defaults make about it.
/// </summary>
/// <remarks>
/// <para>
/// Every module that posts names its accounts through a setting, and every one of those settings
/// ships with a default. The defaults are only meaningful if they point at accounts that exist,
/// take entries, and mean what the setting says they mean.
/// </para>
/// <para>
/// The duplicate check is here because of a real mistake: an exchange-loss account was added on a
/// number the chart already used for cash rounding, so exchange losses would have been posted to
/// the till's rounding figure. Nothing anywhere would have complained — the account existed, it
/// took entries, and the books balanced. Only the meaning was wrong.
/// </para>
/// </remarks>
public sealed class ChartOfAccountsTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-0000-0000-0000-0000000000c1");
    private static readonly Guid Company = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");

    private static readonly List<GlAccount> Chart = FinanceSeeder.ChartOfAccounts(Tenant, Company);

    /// <summary>Every module ASAP ships.</summary>
    private static readonly IAsapModule[] Modules =
    [
        new PlatformModule(),
        new ASAP.Modules.Finance.FinanceModule(),
        new ASAP.Modules.Inventory.InventoryModule(),
        new ASAP.Modules.Purchasing.PurchasingModule(),
        new ASAP.Modules.Promotions.PromotionsModule(),
        new ASAP.Modules.Hr.HrModule(),
        new ASAP.Modules.Sales.SalesModule(),
        new ASAP.Modules.Pos.PosModule(),
    ];

    [Fact]
    public void No_account_number_is_used_twice()
    {
        var duplicates = Chart
            .GroupBy(static a => a.No, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => $"{g.Key} — {string.Join(" / ", g.Select(static a => a.Name))}")
            .ToList();

        duplicates.ShouldBeEmpty(
            $"{duplicates.Count} account number(s) are defined more than once, which makes one "
            + $"account with two meanings and posts one module's figures into another's:"
            + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", duplicates));
    }

    [Fact]
    public void Every_posting_account_a_setting_defaults_to_exists_and_takes_entries()
    {
        var byNumber = Chart.ToDictionary(static a => a.No, StringComparer.OrdinalIgnoreCase);
        var broken = new List<string>();

        foreach (var setting in Modules.SelectMany(static m => m.Setups).Where(IsAccountSetting))
        {
            if (setting.DefaultValue is not { Length: > 0 } number)
            {
                continue;
            }

            if (!byNumber.TryGetValue(number, out var account))
            {
                broken.Add($"{setting.Key} defaults to {number}, which is not in the chart");
                continue;
            }

            // A heading or a total takes no entries at all, so a module defaulting to one would
            // refuse every posting it ever tried to make.
            if (account.AccountType is not GlAccountType.Posting)
            {
                broken.Add($"{setting.Key} defaults to {number} {account.Name}, which is a {account.AccountType}");
            }
        }

        broken.ShouldBeEmpty(
            $"{broken.Count} posting default(s) do not name a usable account:"
            + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", broken));
    }

    /// <summary>
    /// Whether a setting names a general ledger account.
    /// </summary>
    /// <remarks>
    /// By its key rather than by its value. A setting whose default happens to look like a number
    /// -- a threshold, a count of days -- is not an account, and a check that guessed from the
    /// value would report every one of those as broken.
    /// </remarks>
    private static bool IsAccountSetting(SetupDescriptor setting)
        => setting.Key.EndsWith("Account", StringComparison.Ordinal);
}
