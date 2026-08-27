using System.Text.Json.Serialization;
using ASAP.Api;
using ASAP.Api.Endpoints;
using ASAP.Api.Infrastructure;
using ASAP.Api.Security;
using ASAP.Api.Seed;
using ASAP.Modules.Finance.Seed;
using ASAP.Platform.Core;
using ASAP.Platform.Core.Cqrs;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Core.Time;
using ASAP.Platform.Kernel.Messaging;
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

// Enums travel as names, not numbers. A client reading "Sale" can be written against the API by
// someone reading the documentation; a client reading 1 has to keep a copy of the enum in step
// with ours, and will not notice when a value is inserted in the middle.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

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

// Modules. Built-in ones come from AsapModules, the single list the migration tooling also
// reads, so a module cannot be present at runtime and missing from the schema. Extensions are
// discovered from disk by the extension host, which is wired in a later step.
foreach (var schema in AsapModules.Schemas)
{
    builder.Services.AddSingleton(schema);
}

var moduleCatalog = builder.Services.AddAsapCore(builder.Configuration, AsapModules.BuiltIn);

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

// The Angular dev server runs on its own origin, so the browser treats every API call as
// cross-origin. Named and restricted to that origin rather than left open: a policy that allows
// any origin with credentials is one nobody remembers to tighten before going live.
builder.Services.AddCors(options => options.AddPolicy(
    "asap-client",
    policy => policy
        .WithOrigins(
            "http://localhost:4200",
            "https://localhost:4200",
            "http://localhost:4300",
            "https://localhost:4300")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseCors("asap-client");
}

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
app.MapFinanceEndpoints();
app.MapPartyEndpoints();
app.MapInventoryEndpoints();
app.MapTransferEndpoints();
app.MapPurchasingEndpoints();
app.MapSalesEndpoints();
app.MapPromotionsEndpoints();
app.MapHrEndpoints();
app.MapPosEndpoints();
app.MapSyncEndpoints();
app.MapOrganisationEndpoints();
app.MapSetupEndpoints();
app.MapAdminEndpoints();
app.MapNavigationEndpoints();

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
        AuditOverridePermissions(moduleCatalog, services.GetRequiredService<IMessageCatalog>(), logger);

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

        // After the seed, so a fresh install gets its sets built once rather than built and then
        // immediately reconciled. On every later start this is what grants a newly installed
        // module its permissions to the shipped sets -- without it, a customer who buys Inventory
        // gets the module and none of the screens, with no error to explain why.
        await services.GetRequiredService<SystemPermissionSetSynchroniser>()
            .SynchroniseAsync()
            .ConfigureAwait(false);

        await SeedModulesAsync(services, context, logger).ConfigureAwait(false);

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
    /// Lets each module set itself up in any company that has not had it yet.
    /// </summary>
    /// <remarks>
    /// Runs per company rather than once, because a module can be installed long after companies
    /// exist. A company created before Finance arrived needs its chart of accounts on the first
    /// start after the module appears, not never.
    /// </remarks>
    private static async Task SeedModulesAsync(
        IServiceProvider services,
        AsapDbContext context,
        ILogger logger)
    {
        var companies = await context.Companies
            .Where(c => c.IsActive)
            .Select(static c => new { c.Id, c.TenantId, c.Code })
            .ToListAsync()
            .ConfigureAwait(false);

        var financeSeeder = services.GetRequiredService<FinanceSeeder>();
        var inventorySeeder = services.GetRequiredService<ASAP.Modules.Inventory.Seed.InventorySeeder>();
        var posSeeder = services.GetRequiredService<ASAP.Modules.Pos.Seed.PosSeeder>();
        var year = services.GetRequiredService<IClock>().Today.Year;

        foreach (var company in companies)
        {
            var seeded = await financeSeeder
                .SeedAsync(company.TenantId, company.Id, year)
                .ConfigureAwait(false);

            if (seeded)
            {
                logger.LogInformation("Set up Finance for company {Company}.", company.Code);
            }

            if (await inventorySeeder.SeedAsync(company.TenantId, company.Id).ConfigureAwait(false))
            {
                logger.LogInformation("Set up Inventory for company {Company}.", company.Code);
            }

            // After Inventory, which is not incidental: a till sells from a stock location, and
            // one seeded before there were any would have nowhere to sell from.
            if (await posSeeder.SeedAsync(company.TenantId, company.Id).ConfigureAwait(false))
            {
                logger.LogInformation("Set up point of sale for company {Company}.", company.Code);
            }
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

    /// <summary>
    /// Checks that every override a message offers is a permission somebody can actually hold.
    /// </summary>
    /// <remarks>
    /// A blocking message names the permission that would let a user push past it, and the text
    /// tells them to go and find someone who holds it. If no module declares that permission, the
    /// advice sends them on an errand that cannot succeed -- and nobody notices, because the
    /// message only appears when something is already going wrong.
    /// </remarks>
    private static void AuditOverridePermissions(
        IModuleCatalog moduleCatalog,
        IMessageCatalog messages,
        ILogger logger)
    {
        var declared = moduleCatalog.Modules
            .SelectMany(static m => m.Permissions)
            .Select(static p => p.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dangling = messages.All
            .Where(static d => d.OverridePermission is not null)
            .Where(d => !declared.Contains(d.OverridePermission!))
            .ToList();

        foreach (var definition in dangling)
        {
            logger.LogWarning(
                "Message {Code} offers override permission {Permission}, which no module declares. "
                + "Either declare it or remove the offer -- as it stands the message tells the "
                + "user to find an approver who cannot exist.",
                definition.Code.Value,
                definition.OverridePermission);
        }

        logger.LogInformation(
            "Override audit: {Overridable} overridable message(s), {Dangling} naming a permission nobody declares.",
            messages.All.Count(static d => d.OverridePermission is not null),
            dangling.Count);
    }
}

/// <summary>
/// Names the host assembly so integration tests can spin it up through
/// <c>WebApplicationFactory</c>. Top-level statements generate an internal entry point, which a
/// test project cannot reach without this.
/// </summary>
public partial class Program;
