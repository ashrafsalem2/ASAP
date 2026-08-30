using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;

namespace ASAP.Bridge;

/// <summary>A port a device is wired to.</summary>
public interface IDevicePort : IDisposable
{
    /// <summary>What the port is called, as configured.</summary>
    string Name { get; }

    /// <summary>Sends bytes to the device.</summary>
    /// <param name="bytes">What to send.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Nothing.</returns>
    Task WriteAsync(byte[] bytes, CancellationToken cancellationToken = default);

    /// <summary>Reads a line back, or null when nothing arrived in time.</summary>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The line, or null.</returns>
    Task<string?> ReadLineAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Opens ports by name.</summary>
public interface IDevicePortFactory
{
    /// <summary>Whether ports opened here reach real hardware.</summary>
    bool IsReal { get; }

    /// <summary>Opens a port, or returns the one already open for that name.</summary>
    /// <param name="name">The port, such as <c>COM3</c> or <c>/dev/ttyUSB0</c>.</param>
    /// <returns>The port.</returns>
    IDevicePort Open(string name);
}

/// <summary>
/// A real serial port.
/// </summary>
/// <remarks>
/// Ports are kept open and shared by name. Opening a port per request works until two requests
/// overlap, at which point the second is refused by the operating system and a cash drawer does
/// not open for no reason anybody can see from the outside.
/// </remarks>
public sealed class SerialDevicePort : IDevicePort
{
    private readonly SerialPort _port;

    /// <summary>Opens a serial port.</summary>
    /// <param name="name">The port name.</param>
    /// <param name="baudRate">The speed, which has to match what the device expects.</param>
    public SerialDevicePort(string name, int baudRate = 9600)
    {
        Name = name;

        _port = new SerialPort(name, baudRate)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000,
            Encoding = Encoding.ASCII,
        };

        _port.Open();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task WriteAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        await _port.BaseStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await _port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string?> ReadLineAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        deadline.CancelAfter(timeout);

        var buffer = new byte[128];
        var text = new StringBuilder();

        try
        {
            while (!deadline.Token.IsCancellationRequested)
            {
                var read = await _port.BaseStream
                    .ReadAsync(buffer, deadline.Token)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    continue;
                }

                text.Append(Encoding.ASCII.GetString(buffer, 0, read));

                // Most scales end a reading with a carriage return, a line feed or both. Taking
                // whichever arrives first means a scale that sends only one of them still reads.
                var line = text.ToString();
                var at = line.IndexOfAny(['\r', '\n']);

                if (at >= 0)
                {
                    return line[..at].Trim();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A device that says nothing is a device that says nothing. Returning what arrived so
            // far lets a scale that does not terminate its line still be read.
        }

        var partial = text.ToString().Trim();

        return partial.Length > 0 ? partial : null;
    }

    /// <inheritdoc />
    public void Dispose() => _port.Dispose();
}

/// <summary>
/// A port that records what would have been sent and answers plausibly.
/// </summary>
/// <remarks>
/// <para>
/// Not a testing convenience. A shop being set up, a developer building a till screen and a
/// support engineer reproducing a fault all need the software to run without a cash drawer on the
/// desk, and the honest way to provide that is to say plainly that nothing was driven rather than
/// to pretend it was.
/// </para>
/// <para>
/// Which is why every response from a simulated bridge says it is simulated. A demonstration that
/// looks exactly like the real thing is a demonstration somebody will believe on a day it matters.
/// </para>
/// </remarks>
public sealed class SimulatedDevicePort(string name) : IDevicePort
{
    private readonly List<byte[]> _written = [];

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <summary>Everything sent to this port, in order.</summary>
    public IReadOnlyList<byte[]> Written => _written;

    /// <summary>What the next read will return, when something has been queued.</summary>
    public string? NextLine { get; set; }

    /// <inheritdoc />
    public Task WriteAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        _written.Add(bytes);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ReadLineAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        // A plausible reading rather than a refusal, so a till screen can be built and shown
        // without a scale. The response says it is simulated, so nobody mistakes it for a weight.
        => Task.FromResult<string?>(NextLine ?? "ST,GS,   1.000kg");

    /// <inheritdoc />
    public void Dispose()
    {
        // Nothing to close.
    }
}

/// <summary>Opens real serial ports, keeping one open per name.</summary>
/// <param name="baudRate">The speed to open ports at.</param>
public sealed class SerialDevicePortFactory(int baudRate = 9600) : IDevicePortFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, IDevicePort> _open = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsReal => true;

    /// <inheritdoc />
    public IDevicePort Open(string name)
        => _open.GetOrAdd(name, key => new SerialDevicePort(key, baudRate));

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var port in _open.Values)
        {
            port.Dispose();
        }

        _open.Clear();
    }
}

/// <summary>Hands out simulated ports, keeping one per name so what was sent can be read back.</summary>
public sealed class SimulatedDevicePortFactory : IDevicePortFactory
{
    private readonly ConcurrentDictionary<string, IDevicePort> _open = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsReal => false;

    /// <inheritdoc />
    public IDevicePort Open(string name) => _open.GetOrAdd(name, key => new SimulatedDevicePort(key));
}
