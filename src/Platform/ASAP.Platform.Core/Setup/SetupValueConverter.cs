using System.Globalization;
using ASAP.Platform.Kernel.Setup;

namespace ASAP.Platform.Core.Setup;

/// <summary>
/// Parses and validates setup values against what the module declared.
/// </summary>
/// <remarks>
/// <para>
/// Every setup value is stored as text, whatever its declared type, so one table holds the lot
/// and a module can add a setting without a schema change. This class is the boundary where that
/// text becomes a typed value again, and where a value that does not fit its declaration is
/// refused.
/// </para>
/// <para>
/// Parsing is invariant-culture throughout. A decimal tolerance saved by an accountant working in
/// Arabic and read back by a background job running under a different culture must be the same
/// number, and storing "0.5" only to read back "5" is exactly the sort of defect that surfaces
/// months later as an unexplained rounding difference.
/// </para>
/// </remarks>
public static class SetupValueConverter
{
    /// <summary>
    /// Checks a value fits its declaration.
    /// </summary>
    /// <param name="descriptor">What the module declared.</param>
    /// <param name="value">The value as typed, or null to clear the override.</param>
    /// <returns>
    /// Why the value is unacceptable, or null when it is fine. The text is written for the person
    /// at the setup screen, not for a log.
    /// </returns>
    public static string? Validate(SetupDescriptor descriptor, string? value)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Null clears an override so the wider scope applies again, which is always allowed.
        if (value is null)
        {
            return null;
        }

        switch (descriptor.ValueType)
        {
            case SetupValueType.Boolean:
                return bool.TryParse(value, out _) ? null : "a yes or no value";

            case SetupValueType.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
                {
                    return "a whole number";
                }

                return CheckRange(descriptor, whole);

            case SetupValueType.Decimal:
                if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    return "a number";
                }

                return CheckRange(descriptor, number);

            case SetupValueType.Date:
                return DateOnly.TryParse(value, CultureInfo.InvariantCulture, out _)
                    ? null
                    : "a date in the form 2026-08-26";

            case SetupValueType.Option:
                if (descriptor.AllowedValues.Count == 0)
                {
                    return null;
                }

                return descriptor.AllowedValues.Any(o =>
                        string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase))
                    ? null
                    : $"one of: {string.Join(", ", descriptor.AllowedValues.Select(static o => o.Value))}";

            case SetupValueType.EntityReference:
                return Guid.TryParse(value, out _) ? null : "a reference to an existing record";

            case SetupValueType.Json:
                return LooksLikeJson(value) ? null : "valid JSON";

            case SetupValueType.Text:
            case SetupValueType.Secret:
            default:
                return null;
        }
    }

    /// <summary>
    /// Turns a stored value into the type the caller asked for.
    /// </summary>
    /// <typeparam name="TValue">The type wanted.</typeparam>
    /// <param name="value">The stored text, or null when nothing is set.</param>
    /// <returns>The typed value, or the default when nothing is set.</returns>
    /// <exception cref="InvalidCastException">
    /// The stored text does not convert. This means a value was written that should never have
    /// passed <see cref="Validate"/>, so it is a defect rather than a user error.
    /// </exception>
    public static TValue? Parse<TValue>(string? value)
    {
        if (value is null)
        {
            return default;
        }

        var target = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

        try
        {
            if (target == typeof(string))
            {
                return (TValue)(object)value;
            }

            if (target == typeof(bool))
            {
                return (TValue)(object)bool.Parse(value);
            }

            if (target == typeof(int))
            {
                return (TValue)(object)int.Parse(value, CultureInfo.InvariantCulture);
            }

            if (target == typeof(long))
            {
                return (TValue)(object)long.Parse(value, CultureInfo.InvariantCulture);
            }

            if (target == typeof(decimal))
            {
                return (TValue)(object)decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
            }

            if (target == typeof(DateOnly))
            {
                return (TValue)(object)DateOnly.Parse(value, CultureInfo.InvariantCulture);
            }

            if (target == typeof(Guid))
            {
                return (TValue)(object)Guid.Parse(value);
            }

            if (target.IsEnum)
            {
                return (TValue)Enum.Parse(target, value, ignoreCase: true);
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidCastException(
                $"The stored setup value '{value}' cannot be read as {target.Name}. "
                + "It was written without passing validation, which is a defect in whatever wrote it.",
                ex);
        }

        throw new InvalidCastException(
            $"{target.Name} is not a type ASAP setup values can be read as. "
            + "Use string, bool, int, long, decimal, DateOnly, Guid, or an enum.");
    }

    /// <summary>
    /// Describes what a setting expects, for the message shown when a value is rejected.
    /// </summary>
    /// <param name="descriptor">What the module declared.</param>
    public static string Describe(SetupDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var basis = descriptor.ValueType switch
        {
            SetupValueType.Boolean => "a yes or no value",
            SetupValueType.Integer => "a whole number",
            SetupValueType.Decimal => "a number",
            SetupValueType.Date => "a date",
            SetupValueType.Option => $"one of: {string.Join(", ", descriptor.AllowedValues.Select(static o => o.Value))}",
            SetupValueType.EntityReference => $"a reference to a {descriptor.ReferencedEntityType ?? "record"}",
            SetupValueType.Json => "valid JSON",
            _ => "text",
        };

        return (descriptor.Minimum, descriptor.Maximum) switch
        {
            (not null, not null) => $"{basis} between {descriptor.Minimum} and {descriptor.Maximum}",
            (not null, null) => $"{basis} of at least {descriptor.Minimum}",
            (null, not null) => $"{basis} of at most {descriptor.Maximum}",
            _ => basis,
        };
    }

    private static string? CheckRange(SetupDescriptor descriptor, decimal value)
    {
        if (descriptor.Minimum is { } minimum && value < minimum)
        {
            return $"a value of at least {minimum.ToString(CultureInfo.InvariantCulture)}";
        }

        if (descriptor.Maximum is { } maximum && value > maximum)
        {
            return $"a value of at most {maximum.ToString(CultureInfo.InvariantCulture)}";
        }

        return null;
    }

    /// <summary>
    /// A cheap shape check rather than a full parse.
    /// </summary>
    /// <remarks>
    /// Parsing properly would need a JSON reader on a hot path that runs for every setup save,
    /// to catch a mistake only a developer can make: JSON settings are written by module code,
    /// never typed into the setup screen by a user.
    /// </remarks>
    private static bool LooksLikeJson(string value)
    {
        var trimmed = value.AsSpan().Trim();

        return trimmed.Length >= 2
            && ((trimmed[0] == '{' && trimmed[^1] == '}')
                || (trimmed[0] == '[' && trimmed[^1] == ']'));
    }
}
