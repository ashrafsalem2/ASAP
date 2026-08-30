using System.Globalization;

namespace ASAP.Modules.Finance.Journals;

/// <summary>
/// A step through the calendar, written the way an accountant writes one.
/// </summary>
/// <remarks>
/// <para>
/// <c>1M</c> is a month on. <c>3M</c> is a quarter. <c>1M+CM</c> is a month on and then to the end
/// of that month, which is what "the last day of next month" means and is the recurrence almost
/// every accrual actually wants — the thirty-first, the twenty-eighth, the thirtieth, each in its
/// turn, without anybody maintaining a list of them.
/// </para>
/// <para>
/// The alternative is a plain number of days, and it is wrong in a way that takes months to
/// notice: thirty days on from 31 January is 2 March, and a rent accrual that lands on the second
/// of the month is an accrual somebody has to correct twelve times a year.
/// </para>
/// <para>
/// Borrowed from Business Central, which got this right. The units are D, W, M and Q; a term may
/// be preceded by <c>C</c> to mean "to the end of the current one", so <c>CM</c> is the last day
/// of this month and <c>CW</c> the last day of this week.
/// </para>
/// </remarks>
public readonly record struct DateFormula
{
    private readonly List<Term> _terms;

    private DateFormula(List<Term> terms) => _terms = terms;

    /// <summary>Whether the formula says nothing, and so moves no date.</summary>
    public bool IsEmpty => _terms is null || _terms.Count == 0;

    /// <summary>
    /// Reads a formula.
    /// </summary>
    /// <param name="expression">The formula, such as <c>1M</c> or <c>1M+CM</c>.</param>
    /// <param name="formula">The formula when it could be read.</param>
    /// <returns>True when the whole expression was understood.</returns>
    /// <remarks>
    /// All or nothing. A formula that is half understood would advance a recurring journal by
    /// some amount nobody intended, and it would do it every month until somebody noticed.
    /// </remarks>
    public static bool TryParse(string? expression, out DateFormula formula)
    {
        formula = new DateFormula([]);

        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var terms = new List<Term>();
        var at = 0;
        var text = expression.Trim().ToUpperInvariant();

        while (at < text.Length)
        {
            var sign = 1;

            if (text[at] is '+' or '-')
            {
                sign = text[at] == '-' ? -1 : 1;
                at++;
            }

            if (at >= text.Length)
            {
                return false;
            }

            // "C" means the end of the current unit rather than a step of it: CM is the last day
            // of this month, and it is what makes "the last day of next month" expressible as
            // 1M+CM without anybody knowing how long the month is.
            var toEnd = text[at] == 'C';

            if (toEnd)
            {
                at++;
            }

            var start = at;

            while (at < text.Length && char.IsDigit(text[at]))
            {
                at++;
            }

            var count = start == at
                ? 1
                : int.Parse(text[start..at], CultureInfo.InvariantCulture);

            if (at >= text.Length)
            {
                return false;
            }

            var unit = text[at] switch
            {
                'D' => Unit.Day,
                'W' => Unit.Week,
                'M' => Unit.Month,
                'Q' => Unit.Quarter,
                'Y' => Unit.Year,
                _ => Unit.None,
            };

            if (unit is Unit.None)
            {
                return false;
            }

            at++;
            terms.Add(new Term(sign * count, unit, toEnd));
        }

        formula = new DateFormula(terms);

        return terms.Count > 0;
    }

    /// <summary>
    /// Applies the formula to a date.
    /// </summary>
    /// <param name="from">The date to step from.</param>
    /// <returns>The date the formula lands on.</returns>
    public DateOnly From(DateOnly from)
    {
        if (IsEmpty)
        {
            return from;
        }

        var at = from;

        foreach (var term in _terms)
        {
            at = term.Apply(at);
        }

        return at;
    }

    /// <summary>The formula as it was written.</summary>
    /// <returns>The expression.</returns>
    public override string ToString()
        => IsEmpty ? string.Empty : string.Concat(_terms.Select(static t => t.ToString()));

    private enum Unit
    {
        None,
        Day,
        Week,
        Month,
        Quarter,
        Year,
    }

    private readonly record struct Term(int Count, Unit Unit, bool ToEnd)
    {
        public DateOnly Apply(DateOnly from)
        {
            if (ToEnd)
            {
                return EndOf(from);
            }

            return Unit switch
            {
                Unit.Day => from.AddDays(Count),
                Unit.Week => from.AddDays(Count * 7),
                Unit.Month => from.AddMonths(Count),
                Unit.Quarter => from.AddMonths(Count * 3),
                Unit.Year => from.AddYears(Count),
                _ => from,
            };
        }

        /// <summary>The last day of whatever unit the date falls in.</summary>
        private DateOnly EndOf(DateOnly from)
            => Unit switch
            {
                Unit.Day => from,

                // Weeks end on Sunday, which is the convention in the countries this is written
                // for. Somewhere that treats Saturday as the last day wants a setting, not a
                // different formula language.
                Unit.Week => from.AddDays(((int)DayOfWeek.Sunday - (int)from.DayOfWeek + 7) % 7),
                Unit.Month => new DateOnly(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month)),
                Unit.Quarter => EndOfQuarter(from),
                Unit.Year => new DateOnly(from.Year, 12, 31),
                _ => from,
            };

        private static DateOnly EndOfQuarter(DateOnly from)
        {
            var month = (((from.Month - 1) / 3) + 1) * 3;

            return new DateOnly(from.Year, month, DateTime.DaysInMonth(from.Year, month));
        }

        public override string ToString()
        {
            var letter = Unit switch
            {
                Unit.Day => "D",
                Unit.Week => "W",
                Unit.Month => "M",
                Unit.Quarter => "Q",
                Unit.Year => "Y",
                _ => string.Empty,
            };

            if (ToEnd)
            {
                return $"C{letter}";
            }

            return Count < 0
                ? string.Create(CultureInfo.InvariantCulture, $"{Count}{letter}")
                : string.Create(CultureInfo.InvariantCulture, $"+{Count}{letter}");
        }
    }
}
