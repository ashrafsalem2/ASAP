using System.Globalization;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>
/// A set of account numbers written the way an accountant writes one.
/// </summary>
/// <remarks>
/// <para>
/// <c>4000..4999</c> is every account from one to the other. <c>6100|6110</c> is those two.
/// <c>4000..4999|4900</c> is both, which is harmless — an account named twice is counted once,
/// because a statement that double-counted whatever somebody happened to list twice would be
/// wrong in a way nobody could see.
/// </para>
/// <para>
/// The syntax is the one the chart of accounts already uses for its totalling accounts, and this
/// is now the single thing that reads it. Two parsers for one syntax is two sets of edge cases,
/// and the second one is always the one nobody tested.
/// </para>
/// <para>
/// Account numbers are compared as text, not as numbers. A chart is free to use <c>1100-A</c>,
/// and a range treated numerically would silently drop it — or worse, decide that <c>1100-A</c>
/// falls outside <c>1000..1999</c> when every reader can see that it does not.
/// </para>
/// </remarks>
public sealed class AccountRange
{
    private readonly List<(string From, string To)> _spans = [];

    private AccountRange()
    {
    }

    /// <summary>Whether the expression named nothing at all.</summary>
    public bool IsEmpty => _spans.Count == 0;

    /// <summary>
    /// Reads a totalling expression.
    /// </summary>
    /// <param name="expression">The expression, or null.</param>
    /// <returns>The range. An unreadable or empty expression gives an empty range.</returns>
    /// <remarks>
    /// Never throws. An expression is written by a person into a text box, and the place to
    /// complain about it is the screen that shows what it selected — not a stack trace halfway
    /// through drawing a balance sheet.
    /// </remarks>
    public static AccountRange Parse(string? expression)
    {
        var range = new AccountRange();

        if (string.IsNullOrWhiteSpace(expression))
        {
            return range;
        }

        foreach (var term in expression.Split(['|', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = term.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            var at = trimmed.IndexOf("..", StringComparison.Ordinal);

            if (at < 0)
            {
                range._spans.Add((trimmed, trimmed));
                continue;
            }

            var from = trimmed[..at].Trim();
            var to = trimmed[(at + 2)..].Trim();

            // An open end is what somebody means by "6000.." -- everything from there on. The
            // alternative reading, "nothing", is never what anybody wants and is silent about it.
            range._spans.Add((
                from.Length == 0 ? string.Empty : from,
                to.Length == 0 ? "￿" : to));
        }

        return range;
    }

    /// <summary>Whether an account number falls in the range.</summary>
    /// <param name="accountNo">The account number.</param>
    /// <returns>True when it does.</returns>
    public bool Contains(string accountNo)
    {
        if (string.IsNullOrWhiteSpace(accountNo))
        {
            return false;
        }

        foreach (var (from, to) in _spans)
        {
            var atLeast = from.Length == 0
                || string.Compare(accountNo, from, StringComparison.OrdinalIgnoreCase) >= 0;

            var atMost = string.Compare(accountNo, to, StringComparison.OrdinalIgnoreCase) <= 0;

            if (atLeast && atMost)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Sums the amounts of every account the range names.
    /// </summary>
    /// <param name="amounts">Amount by account number.</param>
    /// <returns>The total, or nought when the range names nothing that has moved.</returns>
    public decimal Sum(IReadOnlyDictionary<string, decimal> amounts)
    {
        ArgumentNullException.ThrowIfNull(amounts);

        var total = 0m;

        // Walked once per account rather than once per span, so an account inside two overlapping
        // spans is counted once.
        foreach (var (accountNo, amount) in amounts)
        {
            if (Contains(accountNo))
            {
                total += amount;
            }
        }

        return total;
    }

    /// <summary>The range as it was written, for showing back to whoever wrote it.</summary>
    /// <returns>The expression.</returns>
    public override string ToString()
        => string.Join(
            "|",
            _spans.Select(static s => s.From == s.To
                ? s.From
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"{s.From}..{(s.To == "￿" ? string.Empty : s.To)}")));
}
