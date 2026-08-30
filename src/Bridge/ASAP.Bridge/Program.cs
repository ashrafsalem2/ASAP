using System.Reflection;
using ASAP.Bridge;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection(BridgeOptions.Section));

var options = builder.Configuration.GetSection(BridgeOptions.Section).Get<BridgeOptions>()
              ?? new BridgeOptions();

// Loopback only, and not configurable. A bridge reachable from the network is a cash drawer
// anybody on that network can open, and no deployment is worth that.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(options.Port));

builder.Services.AddSingleton<IDevicePortFactory>(_ => options.Simulate
    ? new SimulatedDevicePortFactory()
    : new SerialDevicePortFactory(options.BaudRate));

builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy =>
{
    // Named origins rather than any. A bridge that answered any page would let a tab from
    // anywhere on the internet open this till's drawer while the shop was serving somebody.
    if (options.AllowedOrigins.Count > 0)
    {
        policy.WithOrigins([.. options.AllowedOrigins]).AllowAnyHeader().AllowAnyMethod();
    }
}));

var app = builder.Build();

app.UseCors();

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";

// Every response carries the till and whether anything real was driven. A browser pointed at the
// wrong till, or at a bridge left in simulation, finds out on the first call rather than on the
// day somebody counts the drawer.
app.MapGet("/health", (IOptions<BridgeOptions> settings, IDevicePortFactory ports) => Results.Ok(new
{
    product = "ASAP bridge",
    version,
    stationCode = settings.Value.StationCode,
    simulated = !ports.IsReal,
}))
   .WithName("BridgeHealth")
   .WithSummary("Says which till this is and whether it drives real hardware.");

app.MapPost("/drawer", async (
        DrawerRequest request,
        IOptions<BridgeOptions> settings,
        IDevicePortFactory ports,
        ILogger<Program> logger,
        CancellationToken cancellationToken) =>
    {
        if (Wrong(request.StationCode, settings.Value) is { } refusal)
        {
            return refusal;
        }

        var port = ports.Open(request.Port);

        await port
            .WriteAsync(DeviceCommands.OpenDrawer(request.Pin), cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Opened the drawer on {Port} at {Station}.", request.Port, settings.Value.StationCode);

        return Results.Ok(new { opened = true, port = request.Port, simulated = !ports.IsReal });
    })
   .WithName("OpenDrawer")
   .WithSummary("Pulses the drawer wired to a printer's port.");

app.MapPost("/display", async (
        DisplayRequest request,
        IOptions<BridgeOptions> settings,
        IDevicePortFactory ports,
        CancellationToken cancellationToken) =>
    {
        if (Wrong(request.StationCode, settings.Value) is { } refusal)
        {
            return refusal;
        }

        var port = ports.Open(request.Port);

        await port
            .WriteAsync(
                DeviceCommands.Display(request.Lines, settings.Value.DisplayWidth),
                cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new { shown = request.Lines.Count, simulated = !ports.IsReal });
    })
   .WithName("ShowOnDisplay")
   .WithSummary("Clears a customer display and writes lines to it.");

app.MapPost("/scale", async (
        ScaleRequest request,
        IOptions<BridgeOptions> settings,
        IDevicePortFactory ports,
        CancellationToken cancellationToken) =>
    {
        if (Wrong(request.StationCode, settings.Value) is { } refusal)
        {
            return refusal;
        }

        var port = ports.Open(request.Port);

        await port.WriteAsync(DeviceCommands.RequestWeight(), cancellationToken).ConfigureAwait(false);

        var reply = await port
            .ReadLineAsync(
                TimeSpan.FromMilliseconds(settings.Value.ScaleTimeoutMilliseconds),
                cancellationToken)
            .ConfigureAwait(false);

        if (!DeviceCommands.TryReadWeight(reply, out var weight, out var settled))
        {
            // What the scale actually said is returned with the refusal. Every scale is slightly
            // different, and the reply is the only thing that tells somebody which one this is.
            return Results.Json(
                new
                {
                    error = "The scale did not send a weight.",
                    reply,
                    simulated = !ports.IsReal,
                },
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Settled is reported, not enforced. A shop weighing something that will not settle still
        // has to sell it, and software that simply refuses is software worked around with a
        // calculator.
        return Results.Ok(new { weight, stable = settled, reply, simulated = !ports.IsReal });
    })
   .WithName("ReadScale")
   .WithSummary("Asks a scale for its current reading.");

app.Run();

// Refuses a request meant for a different till, which is the check that matters most in this
// program. Two tills on one counter, a browser tab left open from yesterday, a copied
// configuration — any of them ends with one till's browser driving another till's drawer, and
// nobody diagnoses that quickly.
static IResult? Wrong(string? asked, BridgeOptions options)
    => string.IsNullOrWhiteSpace(asked)
       || string.Equals(asked, options.StationCode, StringComparison.OrdinalIgnoreCase)
        ? null
        : Results.Json(
            new
            {
                error = "This bridge is not that till.",
                asked,
                stationCode = options.StationCode,
            },
            statusCode: StatusCodes.Status409Conflict);

/// <summary>What a client sends to open a drawer.</summary>
/// <param name="Port">The printer port the drawer is wired to.</param>
/// <param name="StationCode">Which till the caller believes this is.</param>
/// <param name="Pin">Which drawer connector to pulse, 0 or 1.</param>
internal sealed record DrawerRequest(string Port, string? StationCode = null, int Pin = 0);

/// <summary>What a client sends to write to a customer display.</summary>
/// <param name="Port">The display's port.</param>
/// <param name="Lines">What to show, one line each.</param>
/// <param name="StationCode">Which till the caller believes this is.</param>
internal sealed record DisplayRequest(
    string Port,
    IReadOnlyList<string> Lines,
    string? StationCode = null);

/// <summary>What a client sends to read a scale.</summary>
/// <param name="Port">The scale's port.</param>
/// <param name="StationCode">Which till the caller believes this is.</param>
internal sealed record ScaleRequest(string Port, string? StationCode = null);

/// <summary>Lets the test host reach this program.</summary>
public partial class Program;
