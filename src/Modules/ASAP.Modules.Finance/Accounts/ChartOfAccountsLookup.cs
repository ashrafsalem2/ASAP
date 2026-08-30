using ASAP.Platform.Kernel.Accounting;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Finance.Accounts;

/// <summary>
/// Answers what an account is, for modules that must not reference this one.
/// </summary>
/// <remarks>
/// The only thing Finance publishes about its chart. Deliberately narrow: it says what an account
/// is called and whether an entry aimed at it would land, and nothing about balances or structure.
/// A wider port would become the thing other modules reached for instead of raising a posting
/// request, and the ledger would stop being the only place entries are made.
/// </remarks>
/// <param name="context">The unit of work.</param>
public sealed class ChartOfAccountsLookup(AsapDbContext context) : IChartOfAccounts
{
    /// <inheritdoc />
    public async Task<AccountDescription?> DescribeAsync(
        string accountNo,
        CancellationToken cancellationToken = default)
    {
        var normalised = accountNo?.Trim() ?? string.Empty;

        if (normalised.Length == 0)
        {
            return null;
        }

        var account = await context.Set<GlAccount>()
            .AsNoTracking()
            .Where(a => a.No == normalised)
            .Select(static a => new
            {
                a.No,
                a.Name,
                a.NameArabic,
                a.Category,
                a.AccountType,
                a.IsBlocked,
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (account is null)
        {
            return null;
        }

        // Blocked is reported before type, because it is the answer somebody can act on: an
        // account that is only blocked can be unblocked, and one that is a heading never will be.
        var postability = account.IsBlocked
            ? AccountPostability.Blocked
            : account.AccountType is not GlAccountType.Posting
                ? AccountPostability.NotAPostingAccount
                : AccountPostability.Postable;

        return new AccountDescription(
            account.No,
            account.Name,
            account.NameArabic,
            account.Category.ToString(),
            postability);
    }
}
