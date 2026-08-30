using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Results;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Modules.Pos.Stations;

/// <summary>What a till needs installed on it, and what it does not.</summary>
/// <param name="StationCode">The till.</param>
/// <param name="Devices">How many devices it has.</param>
/// <param name="NeedsBridge">Whether any of them needs the bridge agent.</param>
/// <param name="BridgeDevices">Which ones, so the answer is checkable.</param>
public readonly record struct StationReadiness(
    string StationCode,
    int Devices,
    bool NeedsBridge,
    IReadOnlyList<string> BridgeDevices);

/// <summary>
/// The devices at a till, and what setting one up actually requires.
/// </summary>
/// <remarks>
/// The question this exists to answer is "what do I have to install on this till", and for most
/// shops the answer is nothing. A receipt printer goes through the browser's print dialog and a
/// scanner types what it read; between them that is a working shop. Only wired devices — a cash
/// drawer, a display, a scale — need a program on the till, and saying so per till is what keeps
/// "install our agent" from being the answer to every question.
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="messages">Renders refusals.</param>
public sealed class DeviceService(AsapDbContext context, IMessageCatalog messages)
{
    /// <summary>
    /// Lists the devices at one till, or at every till.
    /// </summary>
    /// <param name="stationCode">The till, or null for all of them.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The devices, ordered by till and then by kind.</returns>
    public async Task<List<PosDevice>> ListAsync(
        string? stationCode = null,
        CancellationToken cancellationToken = default)
    {
        var normalised = stationCode?.Trim().ToUpperInvariant();

        var query = context.Set<PosDevice>()
            .AsNoTracking()
            .Include(d => d.Station)
            .Where(d => normalised == null || d.Station!.Code == normalised);

        return await query
            .OrderBy(d => d.Station!.Code)
            .ThenBy(d => d.Kind)
            .ThenBy(d => d.Code)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Says what a till needs installed on it before it can trade.
    /// </summary>
    /// <param name="stationCode">The till.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The answer, or why the till could not be found.</returns>
    public async Task<Result<StationReadiness>> ReadinessAsync(
        string stationCode,
        CancellationToken cancellationToken = default)
    {
        var normalised = stationCode.Trim().ToUpperInvariant();

        var station = await context.Set<PosStation>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == normalised, cancellationToken)
            .ConfigureAwait(false);

        if (station is null)
        {
            return Result<StationReadiness>.Failure(messages.Render(
                PosMessages.DeviceStationNotFound, Args(("Station", normalised))));
        }

        var devices = await context.Set<PosDevice>()
            .AsNoTracking()
            .Where(d => d.StationId == station.Id && d.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var bridged = devices
            .Where(static d => d.NeedsBridge)
            .Select(static d => $"{d.Code} ({d.Kind})")
            .ToList();

        return Result<StationReadiness>.Success(new StationReadiness(
            station.Code,
            devices.Count,
            bridged.Count > 0,
            bridged));
    }

    /// <summary>
    /// Adds a device to a till, or replaces one.
    /// </summary>
    /// <param name="stationCode">The till it belongs to.</param>
    /// <param name="device">What to save. Its code identifies it within the till.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The device, or every reason it was refused.</returns>
    public async Task<Result<PosDevice>> SaveAsync(
        string stationCode,
        PosDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var normalisedStation = stationCode.Trim().ToUpperInvariant();

        var station = await context.Set<PosStation>()
            .FirstOrDefaultAsync(s => s.Code == normalisedStation, cancellationToken)
            .ConfigureAwait(false);

        if (station is null)
        {
            return Result<PosDevice>.Failure(messages.Render(
                PosMessages.DeviceStationNotFound, Args(("Station", normalisedStation))));
        }

        var code = device.Code.Trim().ToUpperInvariant();
        var found = new List<AsapMessage>();

        // A device reached over the network or a wire has to say where it is. One that goes
        // through the browser does not: the print dialog asks the person at the till, which is
        // the right place for that question and the reason a receipt printer needs no setup.
        if (device.Connection is not DeviceConnection.Browser
            && string.IsNullOrWhiteSpace(device.Address))
        {
            return Result<PosDevice>.Failure(messages.Render(
                PosMessages.DeviceNeedsAddress,
                Args(("Device", code), ("Connection", device.Connection.ToString()))));
        }

        var existing = await context.Set<PosDevice>()
            .Where(d => d.StationId == station.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var saved = existing.Find(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));

        if (saved is null)
        {
            saved = new PosDevice
            {
                TenantId = station.TenantId,
                CompanyId = station.CompanyId,
                StationId = station.Id,
                Code = code,
                Name = device.Name,
            };

            context.Set<PosDevice>().Add(saved);
        }

        saved.Name = device.Name;
        saved.NameArabic = device.NameArabic;
        saved.Kind = device.Kind;
        saved.Connection = device.Connection;
        saved.Address = device.Address;
        saved.PrintTemplateCode = device.PrintTemplateCode;
        saved.IsActive = device.IsActive;
        saved.IsDefault = device.IsDefault;

        // Exactly one default per kind per till. A counter with two receipt printers has to know
        // which is meant when nothing says, and two defaults answers that question twice.
        if (saved.IsDefault)
        {
            foreach (var other in existing.Where(d => d.Kind == saved.Kind && d.Id != saved.Id && d.IsDefault))
            {
                other.IsDefault = false;

                found.Add(messages.Render(
                    PosMessages.DeviceDefaultMoved,
                    Args(("Device", other.Code), ("Kind", saved.Kind.ToString()))));
            }
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<PosDevice>.Success(saved, found);
    }

    /// <summary>
    /// Removes a device from a till.
    /// </summary>
    /// <param name="stationCode">The till.</param>
    /// <param name="code">The device.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>Nothing, or why it was refused.</returns>
    public async Task<Result> RemoveAsync(
        string stationCode,
        string code,
        CancellationToken cancellationToken = default)
    {
        var normalisedStation = stationCode.Trim().ToUpperInvariant();
        var normalised = code.Trim().ToUpperInvariant();

        var device = await context.Set<PosDevice>()
            .Include(d => d.Station)
            .FirstOrDefaultAsync(
                d => d.Code == normalised && d.Station!.Code == normalisedStation,
                cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            return Result.Failure(messages.Render(
                PosMessages.DeviceNotFound, Args(("Device", normalised), ("Station", normalisedStation))));
        }

        context.Set<PosDevice>().Remove(device);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] pairs)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in pairs)
        {
            arguments[key] = value;
        }

        return arguments;
    }
}
