namespace ASAP.Bridge;

/// <summary>
/// What this bridge is, and what it is allowed to do.
/// </summary>
/// <remarks>
/// Read from configuration on the till itself rather than fetched from the server, and that is
/// deliberate. Which physical machine this is, and which serial ports are wired to what, is
/// knowledge that lives on the machine — a server that decided it would be a server guessing.
/// </remarks>
public sealed class BridgeOptions
{
    /// <summary>Where the options live in configuration.</summary>
    public const string Section = "Bridge";

    /// <summary>
    /// The till this bridge is attached to.
    /// </summary>
    /// <remarks>
    /// Reported on every response so a browser pointed at the wrong till finds out immediately.
    /// A cash drawer opening in the next shop along is a fault nobody diagnoses quickly.
    /// </remarks>
    public string StationCode { get; set; } = string.Empty;

    /// <summary>The port to listen on, on the loopback interface only.</summary>
    public int Port { get; set; } = 8731;

    /// <summary>
    /// The origins allowed to call this bridge.
    /// </summary>
    /// <remarks>
    /// Named rather than open. A bridge that answered any page would let a browser tab from
    /// anywhere on the internet open the till's cash drawer while the shop was serving somebody.
    /// </remarks>
    public IList<string> AllowedOrigins { get; } = [];

    /// <summary>
    /// Whether to drive real hardware, or to record what would have been sent.
    /// </summary>
    /// <remarks>
    /// Simulated by default, so that installing this and getting it wrong does nothing rather
    /// than something unexpected. A shop turns it on when the ports are known to be right.
    /// </remarks>
    public bool Simulate { get; set; } = true;

    /// <summary>The speed to open serial ports at.</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>How wide a customer display is, in characters.</summary>
    public int DisplayWidth { get; set; } = 20;

    /// <summary>How long to wait for a scale to answer.</summary>
    public int ScaleTimeoutMilliseconds { get; set; } = 2000;
}
