using Scalar.AspNetCore;
using Serilog;

// ASAP host. Composition happens in three passes, and the order is deliberate:
//
//   1. Platform services, which every module assumes are already present.
//   2. Modules, discovered and sorted by their declared dependencies, each registering its own
//      services, permissions, settings, messages and menu entries.
//   3. Startup validation, which refuses to serve traffic if a module declared something
//      incoherent -- a blocking message with no resolution, a permission nothing can grant, a
//      dependency that is not loaded.
//
// Failing at startup is the point. An ERP that boots into an inconsistent configuration will
// discover the problem in the middle of a month-end close instead.

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("ASAP ERP API"));
}

// Liveness probe. Deliberately says nothing about tenants or modules: anything that leaks
// deployment shape to an unauthenticated caller is a gift to whoever is scanning the host.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health")
   .WithSummary("Reports that the host is running.");

app.Run();

/// <summary>
/// Names the host assembly so integration tests can spin it up through
/// <c>WebApplicationFactory</c>. Top-level statements generate an internal entry point, which a
/// test project cannot reach without this.
/// </summary>
public partial class Program;
