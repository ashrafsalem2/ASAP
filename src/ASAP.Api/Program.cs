using ASAP.Api.Endpoints;
using ASAP.Api.Infrastructure;
using ASAP.Api.Security;
using ASAP.Api.Seed;
using ASAP.Platform.Core;
using ASAP.Platform.Core.Cqrs;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Kernel.Tenancy;
using ASAP.Platform.Kernel.Time;
using ASAP.Platform.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

// ASAP host. Composition happens in a deliberate order:
//
//   1. Platform infrastructure, which every module assumes is already present.
//   2. Modules, sorted by their declared dependencies, each registering its own services,
//      permissions, settings, messages and menu entries.
//   3. Startup validation, which refuses to serve traffic when a module declared something
//      incoherent -- a blocking message with no resolution, a duplicated permission key, a
//      dependency that is not loaded.
//
// Failing at startup is the point. An ERP that boots into an inconsistent configuration will
// discover the problem in the middle of a month-end close instead.

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

var connectionString = builder.Configuration.GetConnectionString("Asap")
    ?? throw new InvalidOperationException(
        "No connection string named 'Asap'. Set ConnectionStrings:Asap in configuration.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AsapExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        static o => !o.SigningKey.Contains("CHANGE-ME", StringComparison.OrdinalIgnoreCase),
        "Asap:Jwt:SigningKey is still the development placeholder. A guessable signing key lets "
        + "anyone mint a token for any user in any company. Set a real key through user secrets "
        + "or the environment.")
    .ValidateOnStart();

builder.Services.Configure<SignInPolicyOptions>(
    builder.Configuration.GetSection(SignInPolicyOptions.SectionName));

builder.Services.AddSingleton<IClock>(_ => new SystemClock());

// Both are scoped and both read the current request. The tenant context is registered by its
// concrete type as well, so the seeder can reach BeginCrossTenantScope while module code, which
// only ever sees ITenantContext, cannot.
builder.Services.AddScoped<HttpTenantContext>();
builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpTenantContext>());
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.AddScoped<IUserPermissionSource, UserPermissionSource>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthenticationService>();
builder.Services.AddScoped<DemoSeeder>();

builder.Services.AddAsapPersistence(connectionString);

// Modules. Built-in ones are listed; extensions are discovered from disk by the extension host,
// which is wired in a later step.
IReadOnlyList<IAsapModule> modules = [new PlatformModule()];
var moduleCatalog = builder.Services.AddAsapCore(builder.Configuration, modules);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException($"Configuration section '{JwtOptions.SectionName}' is missing.");

        options.TokenValidationParameters = TokenService.ValidationParameters(jwt);

        // Tokens travel over TLS in production; allowing plain HTTP in development is what makes
        // a local Angular dev server workable.
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("ASAP ERP API"));
}

// Liveness probe. Deliberately says nothing about tenants, modules or versions: anything that
// leaks deployment shape to an unauthenticated caller is a gift to whoever is scanning the host.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .AllowAnonymous()
   .WithName("Health")
   .WithSummary("Reports that the host is running.");

app.MapAuthEndpoints();

await StartupTasks.RunAsync(app, moduleCatalog).ConfigureAwait(false);

app.Run();

/// <summary>Work that runs once, after the container is built and before traffic is served.</summary>
internal static class StartupTasks
{
    /// <summary>
    /// Applies migrations, seeds an empty database, and audits what the loaded modules declared.
    /// </summary>
    /// <param name="app">The built application.</param>
    /// <param name="moduleCatalog">The loaded modules.</param>
    public static async Task RunAsync(WebApplication app, IModuleCatalog moduleCatalog)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();

        logger.LogInformation(
            "Modules loaded in order: {Modules}",
            string.Join(" -> ", moduleCatalog.Modules.Select(static m => m.ModuleId)));

        AuditPermissions(moduleCatalog, logger);

        // The seeder writes rows for a tenant that does not exist yet, so the company filters
        // have to stand aside. The scope is opened here, in the host, rather than being reachable
        // from module code.
        var tenantContext = services.GetRequiredService<HttpTenantContext>();
        using var crossTenant = tenantContext.BeginCrossTenantScope();

        var context = services.GetRequiredService<AsapDbContext>();
        await context.Database.MigrateAsync().ConfigureAwait(false);

        var configuredPassword = app.Configuration["Asap:Seed:AdminPassword"];
        var seeder = services.GetRequiredService<DemoSeeder>();
        var generated = await seeder.SeedAsync(configuredPassword).ConfigureAwait(false);

        if (generated is not null)
        {
            // Written to the log once, on first run only. There is nowhere else it can go: the
            // hash is one-way by design, so an unrecorded generated password locks the
            // installation out of itself.
            logger.LogWarning(
                "Seeded administrator 'admin' with generated password: {Password} "
                + "-- change it on first sign-in. This is shown once and cannot be recovered.",
                generated);
        }
    }

    /// <summary>
    /// Reports every request that guards itself and every request that does not.
    /// </summary>
    /// <remarks>
    /// The weakness of declarative permissions is that forgetting the attribute leaves an
    /// operation open and nothing complains. This is what complains.
    /// </remarks>
    private static void AuditPermissions(IModuleCatalog moduleCatalog, ILogger logger)
    {
        var assemblies = moduleCatalog.Modules
            .Select(static m => m.GetType().Assembly)
            .Distinct()
            .ToList();

        var reports = PermissionAudit.AuditAll(assemblies);
        var undeclared = reports.Where(static r => r.IsUndeclared).ToList();

        logger.LogInformation(
            "Permission audit: {Total} request(s), {Guarded} guarded, {Open} deliberately open.",
            reports.Count,
            reports.Count(static r => r.RequiredPermissions.Count > 0),
            reports.Count(static r => r.DeliberatelyOpenReason is not null));

        foreach (var report in undeclared)
        {
            logger.LogWarning(
                "{Request} declares no permission. Add [RequiresPermission], or "
                + "[NoPermissionRequired(\"reason\")] if that is deliberate.",
                report.RequestType.FullName);
        }
    }
}

/// <summary>
/// Names the host assembly so integration tests can spin it up through
/// <c>WebApplicationFactory</c>. Top-level statements generate an internal entry point, which a
/// test project cannot reach without this.
/// </summary>
public partial class Program;
