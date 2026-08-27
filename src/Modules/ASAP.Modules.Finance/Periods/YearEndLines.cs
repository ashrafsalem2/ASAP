using ASAP.Modules.Finance.Journals;

namespace ASAP.Modules.Finance.Periods;

/// <summary>What an income statement account held at the end of a year, at one branch.</summary>
/// <param name="AccountNo">The account.</param>
/// <param name="BranchId">The branch it was carried at, or null where it named none.</param>
/// <param name="Amount">The signed balance. Debits positive, credits negative.</param>
public readonly record struct YearEndBalance(string AccountNo, Guid? BranchId, decimal Amount);

/// <summary>
/// Turns a year's closing balances into the entries that clear them.
/// </summary>
/// <remarks>
/// Separated from the routine that runs it so the one piece of arithmetic here can be read and
/// tested without a database, a posting engine or a fiscal calendar. Everything else in a year
/// end is guard rails; this is the part that has to be right.
/// </remarks>
public static class YearEndLines
{
    /// <summary>
    /// Builds the transfer.
    /// </summary>
    /// <param name="balances">What each income statement account holds, per branch.</param>
    /// <param name="retainedEarningsAccountNo">Where the result goes.</param>
    /// <param name="description">What every line should say.</param>
    /// <param name="postingDate">The day to post on, which is the year's last.</param>
    /// <returns>
    /// The lines, and the result. A profit is positive. No balances means no lines: a year with
    /// no trading has nothing to transfer and posting a pair of zeroes would say otherwise.
    /// </returns>
    public static (IReadOnlyList<PostJournalLine> Lines, decimal Result) For(
        IEnumerable<YearEndBalance> balances,
        string retainedEarningsAccountNo,
        string description,
        DateOnly postingDate)
    {
        ArgumentNullException.ThrowIfNull(balances);
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedEarningsAccountNo);

        var lines = new List<PostJournalLine>();
        var net = 0m;

        foreach (var balance in balances)
        {
            if (balance.Amount == 0m)
            {
                // An account that did not move needs no entry, and a zero one would put a row on
                // the ledger saying nothing happened -- on every account, every year.
                continue;
            }

            // The exact opposite of what the account holds, carried at the branch that holds it.
            // Clearing per branch is what stops a shop's revenue account showing a balance the
            // company total says is zero.
            lines.Add(new PostJournalLine(
                balance.AccountNo,
                -balance.Amount,
                description,
                PostingDate: postingDate,
                BranchId: balance.BranchId));

            net += balance.Amount;
        }

        if (lines.Count == 0)
        {
            return ([], 0m);
        }

        // Revenue is a credit and counts negative; expenses count positive. Their sum is the
        // loss, so the result is its opposite -- and posting the sum itself to retained earnings
        // is what balances the transfer, a profit arriving there as the credit it should be.
        lines.Add(new PostJournalLine(
            retainedEarningsAccountNo,
            net,
            description,
            PostingDate: postingDate));

        return (lines, -net);
    }
}
