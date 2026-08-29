using System.Globalization;

namespace ASAP.Modules.Finance.Reporting;

/// <summary>
/// Works out one schedule row's formula from the rows it names.
/// </summary>
/// <remarks>
/// <para>
/// A small expression language, and deliberately small: row names, numbers, the four operators
/// and brackets. <c>R100 - R200</c> is a subtotal. <c>R100 / R10 * 100</c> is a margin. Anything
/// beyond that is a report somebody should be writing in code, and pretending otherwise produces
/// a spreadsheet nobody can audit.
/// </para>
/// <para>
/// Multiplication and division bind tighter than addition and subtraction, because that is what
/// everybody who writes <c>R100 + R200 / 2</c> means. Left-to-right evaluation would be simpler
/// to write and would quietly produce a different number than the person intended, which is the
/// worst trade in this file.
/// </para>
/// </remarks>
public static class ScheduleFormula
{
    /// <summary>The rows a formula refers to.</summary>
    /// <param name="expression">The formula.</param>
    /// <returns>Every row name it names, once each.</returns>
    /// <remarks>
    /// Read before anything is evaluated, so the order rows must be worked out in can be
    /// established and a circle can be reported as a circle rather than as a stack overflow.
    /// </remarks>
    public static IReadOnlyCollection<string> ReferencesIn(string? expression)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in Tokenise(expression))
        {
            if (token.Kind is TokenKind.Row)
            {
                found.Add(token.Text);
            }
        }

        return found;
    }

    /// <summary>
    /// Works the formula out.
    /// </summary>
    /// <param name="expression">The formula.</param>
    /// <param name="rows">
    /// What each row came to. A row that is absent counts as nought — a misspelt name in one
    /// formula should not blank the page. A row that is present and null has no answer, and that
    /// spreads: a total built on a figure nobody could work out is not a figure either.
    /// </param>
    /// <returns>The figure, or null when it has no answer.</returns>
    /// <remarks>
    /// Null rather than nought when a division has nothing to divide by. A margin on no revenue
    /// is not nought per cent — it is a question with no answer, and printing nought states
    /// something false about a month that had no sales.
    /// </remarks>
    public static decimal? Evaluate(string? expression, IReadOnlyDictionary<string, decimal?> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var tokens = Tokenise(expression).ToList();

        if (tokens.Count == 0)
        {
            return 0m;
        }

        var at = 0;
        var value = Sum(tokens, ref at, rows);

        // Anything left is a bracket that never opened, or two numbers side by side. Either way
        // the expression does not mean what it looks like it means, so it gets no answer.
        return at == tokens.Count ? value : null;
    }

    /// <summary>Addition and subtraction, which bind loosest.</summary>
    private static decimal? Sum(List<Token> tokens, ref int at, IReadOnlyDictionary<string, decimal?> rows)
    {
        var value = Product(tokens, ref at, rows);

        while (at < tokens.Count && tokens[at].Kind is TokenKind.Plus or TokenKind.Minus)
        {
            var plus = tokens[at].Kind is TokenKind.Plus;
            at++;

            var right = Product(tokens, ref at, rows);

            if (value is null || right is null)
            {
                return null;
            }

            value = plus ? value + right : value - right;
        }

        return value;
    }

    /// <summary>Multiplication and division, which bind tighter.</summary>
    private static decimal? Product(List<Token> tokens, ref int at, IReadOnlyDictionary<string, decimal?> rows)
    {
        var value = Unary(tokens, ref at, rows);

        while (at < tokens.Count && tokens[at].Kind is TokenKind.Times or TokenKind.Divide)
        {
            var times = tokens[at].Kind is TokenKind.Times;
            at++;

            var right = Unary(tokens, ref at, rows);

            if (value is null || right is null)
            {
                return null;
            }

            if (times)
            {
                value *= right;
                continue;
            }

            if (right == 0m)
            {
                return null;
            }

            value = Math.Round(value.Value / right.Value, 4, MidpointRounding.AwayFromZero);
        }

        return value;
    }

    /// <summary>A leading minus, a bracket, a number or a row.</summary>
    private static decimal? Unary(List<Token> tokens, ref int at, IReadOnlyDictionary<string, decimal?> rows)
    {
        if (at >= tokens.Count)
        {
            return null;
        }

        var token = tokens[at];

        switch (token.Kind)
        {
            case TokenKind.Minus:
                at++;
                return -Unary(tokens, ref at, rows);

            case TokenKind.Plus:
                at++;
                return Unary(tokens, ref at, rows);

            case TokenKind.Open:
                at++;
                var inner = Sum(tokens, ref at, rows);

                if (at < tokens.Count && tokens[at].Kind is TokenKind.Close)
                {
                    at++;
                    return inner;
                }

                return null;

            case TokenKind.Number:
                at++;
                return decimal.TryParse(
                    token.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : null;

            case TokenKind.Row:
                at++;

                // Two different absences, answered differently on purpose.
                //
                // A row nobody defined counts as nought. It is a typo in a formula, the statement
                // is otherwise sound, and stopping the whole page over one misspelt name helps
                // nobody -- the schedule reports the unknown name separately, which is where
                // somebody is looking.
                //
                // A row that exists and has no answer propagates. It is a heading, or a ratio
                // that could not be worked out, and treating it as nought would print a total
                // that looks complete and is not. Same reasoning as dividing by nothing.
                return rows.TryGetValue(token.Text, out var value) ? value : 0m;

            default:
                return null;
        }
    }

    private static IEnumerable<Token> Tokenise(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            yield break;
        }

        var at = 0;

        while (at < expression.Length)
        {
            var c = expression[at];

            if (char.IsWhiteSpace(c))
            {
                at++;
                continue;
            }

            switch (c)
            {
                case '+': yield return new Token(TokenKind.Plus, "+"); at++; continue;
                case '-': yield return new Token(TokenKind.Minus, "-"); at++; continue;
                case '*': yield return new Token(TokenKind.Times, "*"); at++; continue;
                case '/': yield return new Token(TokenKind.Divide, "/"); at++; continue;
                case '(': yield return new Token(TokenKind.Open, "("); at++; continue;
                case ')': yield return new Token(TokenKind.Close, ")"); at++; continue;
                default: break;
            }

            if (char.IsDigit(c) || c == '.')
            {
                var start = at;

                while (at < expression.Length && (char.IsDigit(expression[at]) || expression[at] == '.'))
                {
                    at++;
                }

                yield return new Token(TokenKind.Number, expression[start..at]);
                continue;
            }

            if (char.IsLetter(c))
            {
                var start = at;

                while (at < expression.Length
                       && (char.IsLetterOrDigit(expression[at]) || expression[at] == '_'))
                {
                    at++;
                }

                yield return new Token(TokenKind.Row, expression[start..at]);
                continue;
            }

            // Anything else is a character nobody meant to type. Skipped rather than refused,
            // because the schedule reports what a row could not compute and that is where the
            // person is looking.
            at++;
        }
    }

    private enum TokenKind
    {
        Number,
        Row,
        Plus,
        Minus,
        Times,
        Divide,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);
}
