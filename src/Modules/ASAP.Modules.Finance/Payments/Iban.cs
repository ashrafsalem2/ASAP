using System.Text;

namespace ASAP.Modules.Finance.Payments;

/// <summary>
/// Whether an IBAN is one a bank will accept.
/// </summary>
/// <remarks>
/// <para>
/// Checked with the ISO 13616 check digits, not merely for shape. A mistyped IBAN of the right
/// length looks perfectly plausible on a screen, and the first sign of it otherwise is a payment
/// the bank bounces a week later — or worse, one that lands in somebody else's account because
/// two digits were swapped into a number that also happens to exist. The check digits catch
/// every single-character error and nearly every transposition, which is the whole of what
/// people actually get wrong when they type one.
/// </para>
/// <para>
/// Spaces and lower case are accepted on the way in, because that is how IBANs are printed on
/// invoices, and stripped on the way out, because that is how banks want them in a file.
/// </para>
/// </remarks>
public static class Iban
{
    /// <summary>The lengths by country, for the countries a company here most often pays into.</summary>
    /// <remarks>
    /// Not exhaustive, and deliberately so: a country not listed is still accepted if the check
    /// digits hold. The list exists to catch the commonest error the check digits cannot — a
    /// Saudi IBAN with a digit missing that happens to still divide.
    /// </remarks>
    private static readonly Dictionary<string, int> Lengths = new(StringComparer.Ordinal)
    {
        ["SA"] = 24,
        ["AE"] = 23,
        ["BH"] = 22,
        ["KW"] = 30,
        ["QA"] = 29,
        ["OM"] = 23,
        ["JO"] = 30,
        ["EG"] = 29,
        ["GB"] = 22,
        ["DE"] = 22,
        ["FR"] = 27,
        ["TR"] = 26,
    };

    /// <summary>The IBAN as a bank wants it: upper case, no spaces.</summary>
    /// <param name="value">The IBAN as somebody typed or pasted it.</param>
    /// <returns>The normalised IBAN, or an empty string.</returns>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch) && ch != '-')
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.ToString();
    }

    /// <summary>Whether the IBAN is well formed and its check digits hold.</summary>
    /// <param name="value">The IBAN, in any spacing or case.</param>
    /// <returns>Whether a bank would accept it.</returns>
    public static bool IsValid(string? value)
    {
        var iban = Normalise(value);

        if (iban.Length is < 15 or > 34)
        {
            return false;
        }

        if (!char.IsAsciiLetterUpper(iban[0]) || !char.IsAsciiLetterUpper(iban[1])
            || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
        {
            return false;
        }

        if (Lengths.TryGetValue(iban[..2], out var expected) && iban.Length != expected)
        {
            return false;
        }

        foreach (var ch in iban)
        {
            if (!char.IsAsciiLetterUpper(ch) && !char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        // Move the first four characters to the end, turn letters into numbers (A = 10), and the
        // whole thing taken as one very long number must leave a remainder of 1 when divided by
        // 97. Done a digit at a time, because the number does not fit in anything.
        var rearranged = iban[4..] + iban[..4];
        var remainder = 0;

        foreach (var ch in rearranged)
        {
            if (char.IsAsciiDigit(ch))
            {
                remainder = ((remainder * 10) + (ch - '0')) % 97;
            }
            else
            {
                remainder = ((remainder * 100) + (ch - 'A' + 10)) % 97;
            }
        }

        return remainder == 1;
    }
}
