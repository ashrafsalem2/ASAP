using System.Text;
using ASAP.Bridge;
using Shouldly;

namespace ASAP.Bridge.Tests;

/// <summary>
/// Covers the bytes a device is sent and the sense made of what it sends back.
/// </summary>
/// <remarks>
/// The whole reason these are pure functions. A drawer that opens on a real till and not on a
/// test bench is a drawer nobody can debug, and the alternative to tests here is a developer with
/// a cash drawer on their desk and a shop waiting.
/// </remarks>
public sealed class DeviceCommandTests
{
    [Fact]
    public void The_drawer_pulse_is_the_sequence_the_trade_settled_on()
    {
        var bytes = DeviceCommands.OpenDrawer();

        // ESC p, pin 0, then the two timings.
        bytes[0].ShouldBe((byte)0x1B);
        bytes[1].ShouldBe((byte)0x70);
        bytes[2].ShouldBe((byte)0x00);
    }

    [Fact]
    public void Drawer_timings_are_in_units_of_two_milliseconds()
    {
        // The commonest thing to get wrong. A hundred here is two hundred milliseconds, and a
        // drawer given a hundred as though it were milliseconds barely twitches.
        var bytes = DeviceCommands.OpenDrawer(onMilliseconds: 100, offMilliseconds: 200);

        bytes[3].ShouldBe((byte)50);
        bytes[4].ShouldBe((byte)100);
    }

    [Fact]
    public void A_timing_too_long_for_the_protocol_is_clamped_rather_than_wrapping()
    {
        // A byte that wrapped would turn a long pulse into a short one, which is a drawer that
        // does not open and no error anywhere.
        var bytes = DeviceCommands.OpenDrawer(onMilliseconds: 100_000, offMilliseconds: 100_000);

        bytes[3].ShouldBe((byte)255);
        bytes[4].ShouldBe((byte)255);
    }

    [Fact]
    public void The_second_drawer_connector_is_reachable()
    {
        DeviceCommands.OpenDrawer(pin: 1)[2].ShouldBe((byte)0x01);
    }

    [Fact]
    public void A_display_is_cleared_before_anything_is_written()
    {
        var bytes = DeviceCommands.Display(["Total", "12.50"]);

        // Without the clear, a shorter line leaves the tail of the previous one on screen — and a
        // customer reads a total with somebody else's digits on the end of it.
        bytes[0].ShouldBe((byte)0x0C);
        Encoding.ASCII.GetString(bytes[1..]).ShouldBe("Total\r\n12.50");
    }

    [Fact]
    public void A_line_too_long_for_the_display_is_cut_rather_than_wrapped()
    {
        // A two-line display that wraps shows the second half of the first line where the total
        // should be. A shortened name is the better failure.
        var bytes = DeviceCommands.Display(["Extra mature farmhouse cheddar"], width: 10);

        Encoding.ASCII.GetString(bytes[1..]).ShouldBe("Extra matu");
    }

    [Theory]
    [InlineData("ST,GS,   1.234kg", 1.234, true)]
    [InlineData("US,GS,   0.500kg", 0.500, false)]
    [InlineData("   2.75 kg", 2.75, false)]
    [InlineData("STABLE 0.125", 0.125, true)]
    [InlineData("-0.002kg", -0.002, false)]
    public void A_weight_is_read_out_of_whatever_the_scale_sent(
        string reply,
        double expected,
        bool expectedStable)
    {
        // Scales differ, and this is deliberately not a driver for any one of them: it looks for
        // the first number it can read and for the words that mean settled.
        DeviceCommands.TryReadWeight(reply, out var weight, out var stable).ShouldBeTrue();

        weight.ShouldBe((decimal)expected);
        stable.ShouldBe(expectedStable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ERROR")]
    public void A_reply_with_no_number_in_it_is_not_a_weight(string? reply)
    {
        // Reported rather than guessed at. A scale that says ERROR and a system that reads nought
        // sells something heavy for nothing.
        DeviceCommands.TryReadWeight(reply, out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void Unstable_wins_over_stable_when_a_reply_could_be_read_as_both()
    {
        // "US" and "ST" both appear in some formats. Reading it as settled when it is not puts a
        // moving number on a receipt.
        DeviceCommands.TryReadWeight("US,ST,1.000", out _, out var stable).ShouldBeTrue();

        stable.ShouldBeFalse();
    }

    [Fact]
    public async Task A_simulated_port_records_what_would_have_been_sent()
    {
        var factory = new SimulatedDevicePortFactory();

        factory.IsReal.ShouldBeFalse("a bridge that cannot say it is simulating is one somebody believes");

        var port = (SimulatedDevicePort)factory.Open("COM3");

        await port.WriteAsync(DeviceCommands.OpenDrawer());

        port.Written.ShouldHaveSingleItem()[0].ShouldBe((byte)0x1B);

        // The same name gives the same port, so what was sent can be read back.
        factory.Open("COM3").ShouldBeSameAs(port);
    }

    [Fact]
    public async Task A_simulated_scale_answers_plausibly_so_a_till_screen_can_be_built()
    {
        var port = (SimulatedDevicePort)new SimulatedDevicePortFactory().Open("COM4");

        var reply = await port.ReadLineAsync(TimeSpan.FromMilliseconds(10));

        DeviceCommands.TryReadWeight(reply, out var weight, out _).ShouldBeTrue();
        weight.ShouldBe(1.000m);
    }
}
