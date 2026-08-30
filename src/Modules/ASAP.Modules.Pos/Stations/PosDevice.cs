using ASAP.Platform.Kernel.Entities;

namespace ASAP.Modules.Pos.Stations;

/// <summary>What a device is for.</summary>
public enum DeviceKind
{
    /// <summary>Prints the receipt a customer takes away.</summary>
    ReceiptPrinter = 0,

    /// <summary>Prints shelf edge and product labels.</summary>
    LabelPrinter = 1,

    /// <summary>Reads a barcode.</summary>
    Scanner = 2,

    /// <summary>The drawer the cash goes in.</summary>
    CashDrawer = 3,

    /// <summary>The second screen the customer reads.</summary>
    CustomerDisplay = 4,

    /// <summary>Weighs loose goods.</summary>
    Scale = 5,

    /// <summary>Takes a card payment.</summary>
    PaymentTerminal = 6,
}

/// <summary>
/// How the till software reaches a device.
/// </summary>
/// <remarks>
/// The distinction that decides whether a shop has to install anything. Most of what a till needs
/// is reachable from a browser as it stands, and a system that answers "install our agent" to
/// every device makes a five-minute setup into an IT project.
/// </remarks>
public enum DeviceConnection
{
    /// <summary>
    /// The browser reaches it with no help.
    /// </summary>
    /// <remarks>
    /// A receipt printer driven through the browser's own print dialog, and a scanner, which
    /// almost always presents itself as a keyboard and simply types what it read. Between them
    /// that is most shops, and neither needs anything installed.
    /// </remarks>
    Browser = 0,

    /// <summary>
    /// A printer or terminal on the network, addressed directly.
    /// </summary>
    /// <remarks>
    /// A label printer with its own address, or a payment terminal the till talks to over the
    /// local network. Nothing is installed on the till, but something has to be told where to
    /// find it.
    /// </remarks>
    Network = 1,

    /// <summary>
    /// Reached through a small program running on the till itself.
    /// </summary>
    /// <remarks>
    /// A cash drawer, a customer display, a scale — things wired to a serial or USB port that a
    /// browser cannot open. This is the only case that needs the bridge agent installed, and
    /// keeping it to these devices is the point of naming the connection at all.
    /// </remarks>
    Bridge = 2,
}

/// <summary>
/// One piece of hardware at a till.
/// </summary>
/// <remarks>
/// <para>
/// A station is a named set of devices bound to a branch and a till, which is what lets a shop be
/// set up once and a replacement till be swapped in without anybody reconfiguring the software.
/// The record says what the device is, how it is reached, and where to find it — and nothing at
/// all about how to speak to it, because that belongs in whatever drives it.
/// </para>
/// <para>
/// Naming the connection is the design decision worth defending. Most of what a till needs works
/// from a browser as it stands: receipts print through the print dialog, and a scanner types. Only
/// the wired devices need a program installed on the till, and a system that does not distinguish
/// them ends up answering "install our agent" to a shop that needed nothing.
/// </para>
/// </remarks>
public sealed class PosDevice : CompanyEntity
{
    /// <summary>Short stable code, unique per station.</summary>
    public required string Code { get; set; }

    /// <summary>What it is called.</summary>
    public required string Name { get; set; }

    /// <summary>What it is called in Arabic.</summary>
    public string? NameArabic { get; set; }

    /// <summary>The till it belongs to.</summary>
    public Guid StationId { get; set; }

    /// <summary>Navigation to the till.</summary>
    public PosStation? Station { get; set; }

    /// <summary>What it is for.</summary>
    public DeviceKind Kind { get; set; }

    /// <summary>How the till software reaches it.</summary>
    public DeviceConnection Connection { get; set; }

    /// <summary>
    /// Where to find it: a host and port, a queue name, a serial port.
    /// </summary>
    /// <remarks>
    /// Free text, and deliberately so. What identifies a device differs by every kind of device
    /// there is, and a schema that tried to model all of them would need changing for the next
    /// one. Whatever drives the device knows how to read this; nothing else needs to.
    /// </remarks>
    public string? Address { get; set; }

    /// <summary>
    /// The print template this device uses, when it prints.
    /// </summary>
    /// <remarks>
    /// Named per device rather than per station, because a shop with a receipt printer and a
    /// label printer needs two different layouts and they are chosen by which device is printing.
    /// </remarks>
    public string? PrintTemplateCode { get; set; }

    /// <summary>Whether it is the one to use when a till has more than one of a kind.</summary>
    /// <remarks>
    /// A counter with two receipt printers — one for the customer and one for the kitchen — needs
    /// to know which is meant when nothing says. Exactly one per kind per station is the default.
    /// </remarks>
    public bool IsDefault { get; set; }

    /// <summary>Whether it may still be used.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Whether reaching it needs the bridge agent installed on the till.</summary>
    public bool NeedsBridge => Connection is DeviceConnection.Bridge;
}
