using System.Globalization;
using System.Text;

namespace ASAP.Bridge;

/// <summary>
/// The bytes a device expects, and the sense to be made of what it sends back.
/// </summary>
/// <remarks>
/// <para>
/// Pure functions over bytes, deliberately. Everything hard about talking to a cash drawer is in
/// what to send and what a reply means, and neither of those needs a serial port to be tested. A
/// drawer that opens on a real till and not on a test bench is a drawer nobody can debug.
/// </para>
/// <para>
/// The sequences are the ones the trade has settled on — ESC/POS for printers and drawers, and a
/// weight line for scales — but every one of them is overridable in configuration, because "the
/// trade has settled on" is not the same as "every device agrees".
/// </para>
/// </remarks>
public static class DeviceCommands
{
    /// <summary>
    /// The pulse that opens a cash drawer.
    /// </summary>
    /// <param name="pin">Which drawer connector to pulse, 0 or 1.</param>
    /// <param name="onMilliseconds">How long the pulse is held.</param>
    /// <param name="offMilliseconds">How long to wait after it.</param>
    /// <returns>The bytes to send.</returns>
    /// <remarks>
    /// <para>
    /// A cash drawer has no intelligence and no cable of its own: it is wired into the receipt
    /// printer and opened by a pulse on one of two pins. That is why a drawer is set up against
    /// the printer's port rather than one of its own, and why "the drawer will not open" is
    /// almost always a printer problem.
    /// </para>
    /// <para>
    /// The timings are in units of two milliseconds, which is the ESC/POS convention and the
    /// commonest thing to get wrong. A hundred here is two hundred milliseconds, not a hundred.
    /// </para>
    /// </remarks>
    public static byte[] OpenDrawer(int pin = 0, int onMilliseconds = 50, int offMilliseconds = 200)
    {
        var on = (byte)Math.Clamp(onMilliseconds / 2, 1, 255);
        var off = (byte)Math.Clamp(offMilliseconds / 2, 1, 255);

        return [0x1B, 0x70, (byte)(pin == 0 ? 0x00 : 0x01), on, off];
    }

    /// <summary>
    /// Clears a customer display and writes lines to it.
    /// </summary>
    /// <param name="lines">What to show, one line each.</param>
    /// <param name="width">How many characters fit on a line.</param>
    /// <param name="encoding">How to turn the text into bytes, or null for ASCII.</param>
    /// <returns>The bytes to send.</returns>
    /// <remarks>
    /// Truncated rather than wrapped. A two-line display that wraps shows the second half of the
    /// first line where the total should be, and a customer reading a total that is really the
    /// tail of a product name is worse served than one reading a shortened name.
    /// </remarks>
    public static byte[] Display(IReadOnlyList<string> lines, int width = 20, Encoding? encoding = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var text = encoding ?? Encoding.ASCII;
        var bytes = new List<byte> { 0x0C };

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i] ?? string.Empty;

            bytes.AddRange(text.GetBytes(line.Length > width ? line[..width] : line));

            if (i < lines.Count - 1)
            {
                bytes.AddRange([0x0D, 0x0A]);
            }
        }

        return [.. bytes];
    }

    /// <summary>The byte that asks a scale for its current reading.</summary>
    /// <remarks>
    /// ENQ, which is what most bench scales answer. A scale that streams continuously ignores it
    /// and is read by taking the next line instead.
    /// </remarks>
    public static byte[] RequestWeight() => [0x05];

    /// <summary>
    /// Reads a weight out of whatever a scale sent back.
    /// </summary>
    /// <param name="reply">The line the scale sent.</param>
    /// <param name="weight">The weight, when one could be read.</param>
    /// <param name="stable">Whether the scale said the reading had settled.</param>
    /// <returns>True when a weight was found.</returns>
    /// <remarks>
    /// <para>
    /// Scales differ, but nearly all of them send a status, a number and a unit in some order —
    /// <c>ST,GS,  1.234kg</c> is typical. This looks for the first number it can read and for the
    /// words that mean settled, which covers the common ones without pretending to be a driver
    /// for any particular scale.
    /// </para>
    /// <para>
    /// Stability is reported rather than enforced. A shop weighing something that will not settle
    /// — a live fish, a shaking hand — still has to sell it, and a system that simply refuses is
    /// a system somebody works around with a calculator.
    /// </para>
    /// </remarks>
    public static bool TryReadWeight(string? reply, out decimal weight, out bool stable)
    {
        weight = 0m;
        stable = false;

        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        // "US" is unstable and appears before "ST" would match anything, so it is checked first.
        stable = !reply.Contains("US", StringComparison.OrdinalIgnoreCase)
                 && (reply.Contains("ST", StringComparison.OrdinalIgnoreCase)
                     || reply.Contains("STABLE", StringComparison.OrdinalIgnoreCase));

        var start = -1;
        var end = -1;

        for (var i = 0; i < reply.Length; i++)
        {
            var c = reply[i];

            if (char.IsDigit(c) || (c == '.' && start >= 0) || ((c == '-' || c == '+') && start < 0
                && i + 1 < reply.Length && char.IsDigit(reply[i + 1])))
            {
                if (start < 0)
                {
                    start = i;
                }

                end = i + 1;
                continue;
            }

            if (start >= 0)
            {
                break;
            }
        }

        return start >= 0
               && decimal.TryParse(
                   reply[start..end],
                   NumberStyles.Number,
                   CultureInfo.InvariantCulture,
                   out weight);
    }
}
