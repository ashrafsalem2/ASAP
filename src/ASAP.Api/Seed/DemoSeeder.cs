using System.Security.Cryptography;
using ASAP.Api.Security;
using ASAP.Platform.Core.Dimensions;
using ASAP.Platform.Core.Modules;
using ASAP.Platform.Core.Numbering;
using ASAP.Platform.Core.Security;
using ASAP.Platform.Core.Tenancy;
using ASAP.Platform.Kernel.Modules;
using ASAP.Platform.Kernel.Security;
using ASAP.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ASAP.Api.Seed;

/// <summary>
/// Puts a working company into an empty database: one tenant, one company, three branches, the
/// standard permission sets, an administrator, number series and two dimensions.
/// </summary>
/// <remarks>
/// <para>
/// Runs only against an empty database and does nothing otherwise, so it is safe to leave enabled
/// on every start. It never updates existing rows: a seeder that reasserts its idea of the right
/// data is a seeder that will one day overwrite a customer configuration.
/// </para>
/// <para>
/// This is the demo seed. Creating a real company is a guided setup in the application, not a
/// fixture.
/// </para>
/// </remarks>
/// <param name="context">The unit of work.</param>
/// <param name="moduleCatalog">Supplies the permissions the standard sets are built from.</param>
/// <param name="logger">Reports what was created.</param>
public sealed class DemoSeeder(
    AsapDbContext context,
    IModuleCatalog moduleCatalog,
    ILogger<DemoSeeder> logger)
{
    /// <summary>
    /// Seeds the database if it is empty.
    /// </summary>
    /// <param name="adminPassword">
    /// Password for the administrator. When null, one is generated and returned so the host can
    /// show it once.
    /// </param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The generated administrator password when one was created, otherwise null. Held only long
    /// enough for the host to write it to the console on first run.
    /// </returns>
    public async Task<string?> SeedAsync(string? adminPassword, CancellationToken cancellationToken = default)
    {
        if (await context.Tenants.IgnoreQueryFilters().AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Database already holds a tenant; the demo seed was skipped.");

            // Except the number series, which are topped up by code on every start. Installing a
            // module adds documents that need numbering, and gating that behind "is this database
            // empty" means an established company gets the module, the screens, and a refusal the
            // first time anybody raises one.
            await TopUpNumberSeriesAsync(cancellationToken).ConfigureAwait(false);

            return null;
        }

        logger.LogInformation("Empty database. Seeding the ASAP demo company.");

        var tenant = SeedTenant();
        var company = SeedCompany(tenant);
        var branches = SeedBranches(company);
        var sets = SeedPermissionSets(tenant);
        var generatedPassword = adminPassword is null ? GeneratePassword() : null;
        SeedAdministrator(tenant, company, branches, sets, adminPassword ?? generatedPassword!);
        SeedNumberSeries(company, branches, skip: null);
        SeedDimensions(company);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Seeded tenant {Tenant}, company {Company}, {BranchCount} branches and {SetCount} permission sets.",
            tenant.Code,
            company.Code,
            branches.Count,
            sets.Count);

        return generatedPassword;
    }

    /// <summary>
    /// Adds any number series a company is missing, without touching the ones it has.
    /// </summary>
    /// <remarks>
    /// Adds only. A seeder that reasserts its idea of the right starting number would one day
    /// reset a series a customer had already issued from, and the numbers it reissued would
    /// collide with documents that already exist.
    /// </remarks>
    private async Task TopUpNumberSeriesAsync(CancellationToken cancellationToken)
    {
        var companies = await context.Companies
            .IgnoreQueryFilters()
            .Where(static c => !c.IsDeleted)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var added = 0;

        foreach (var company in companies)
        {
            var existing = await context.Set<NumberSeries>()
                .IgnoreQueryFilters()
                .Where(s => s.CompanyId == company.Id)
                .Select(static s => s.Code)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var have = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var branches = await context.Branches
                .IgnoreQueryFilters()
                .Where(b => b.CompanyId == company.Id && !b.IsDeleted)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var before = context.ChangeTracker.Entries<NumberSeries>().Count();

            SeedNumberSeries(company, branches, have);

            added += context.ChangeTracker.Entries<NumberSeries>().Count() - before;
        }

        if (added == 0)
        {
            return;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Added {Count} number series that had no rows yet.", added);
    }

    private Tenant SeedTenant()
    {
        var tenant = new Tenant
        {
            Code = "DEMO",
            Name = "ASAP Demo Organisation",
            NameArabic = "مؤسسة ASAP التجريبية",
            DefaultCulture = "en",
            TimeZoneId = "Asia/Riyadh",

            // Empty means every loaded module is available, which is what a demo and a
            // single-tenant on-premise install both want.
            LicensedModules = [],
        };

        context.Tenants.Add(tenant);
        return tenant;
    }

    private Company SeedCompany(Tenant tenant)
    {
        var company = new Company
        {
            TenantId = tenant.Id,
            Code = "MAIN",
            Name = "ASAP Trading Company",
            NameArabic = "شركة ASAP للتجارة",
            BaseCurrencyCode = "SAR",
            FiscalYearStartMonth = 1,
        };

        context.Companies.Add(company);
        return company;
    }

    private List<Branch> SeedBranches(Company company)
    {
        List<Branch> branches =
        [
            new()
            {
                TenantId = company.TenantId,
                CompanyId = company.Id,
                Code = "HO",
                Name = "Head Office",
                NameArabic = "المركز الرئيسي",
                Kind = BranchKind.HeadOffice,
                City = "Riyadh",
            },
            new()
            {
                TenantId = company.TenantId,
                CompanyId = company.Id,
                Code = "RUH-01",
                Name = "Riyadh Branch",
                NameArabic = "فرع الرياض",
                Kind = BranchKind.Store,
                City = "Riyadh",
            },
            new()
            {
                TenantId = company.TenantId,
                CompanyId = company.Id,
                Code = "JED-01",
                Name = "Jeddah Branch",
                NameArabic = "فرع جدة",
                Kind = BranchKind.Store,
                City = "Jeddah",
            },
        ];

        context.Branches.AddRange(branches);
        return branches;
    }

    /// <summary>
    /// Creates the standard permission sets from what the loaded modules actually declare.
    /// </summary>
    /// <remarks>
    /// Built by filtering the declared permissions rather than by listing keys. A hand-written
    /// list goes stale the moment a module adds a permission, and the staleness shows up as an
    /// administrator quietly missing an ability nobody thought to add.
    /// </remarks>
    /// <summary>
    /// Creates the shipped permission sets from the rules in
    /// <see cref="SystemPermissionSets"/>, resolved against what the loaded modules declare.
    /// </summary>
    /// <remarks>
    /// The rules live there rather than here so the seeder and the synchroniser cannot drift.
    /// The synchroniser keeps these current as modules are installed afterwards; this only has to
    /// get the first installation right.
    /// </remarks>
    private List<PermissionSet> SeedPermissionSets(Tenant tenant)
    {
        var declared = moduleCatalog.Modules.SelectMany(static m => m.Permissions).ToList();
        var sets = new List<PermissionSet>();

        foreach (var definition in SystemPermissionSets.All)
        {
            var keys = SystemPermissionSets.Resolve(definition, declared);

            // A set that resolves to nothing belongs to a module this installation does not have.
            // An empty Accountant set on a tenant without Finance is a meaningless option on the
            // assignment screen.
            if (keys.Count == 0)
            {
                continue;
            }

            var set = new PermissionSet
            {
                TenantId = tenant.Id,
                Code = definition.Code,
                Name = definition.Name,
                NameArabic = definition.NameArabic,
                Description = definition.Description,

                // Shipped sets are read-only in the UI. An administrator copies one to change it,
                // so an upgrade can refresh the original without discarding their edits.
                IsSystemDefined = true,
            };

            foreach (var key in keys)
            {
                set.Entries.Add(new PermissionSetEntry { PermissionSetId = set.Id, PermissionKey = key });
            }

            sets.Add(set);
        }

        context.PermissionSets.AddRange(sets);
        return sets;
    }

    private void SeedAdministrator(
        Tenant tenant,
        Company company,
        List<Branch> branches,
        List<PermissionSet> sets,
        string password)
    {
        var headOffice = branches.Single(static b => b.Kind == BranchKind.HeadOffice);

        var admin = new User
        {
            TenantId = tenant.Id,
            UserName = "admin",
            DisplayName = "System Administrator",
            Email = "admin@asap.local",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
            Culture = "en",
            IsSuperUser = true,
            DefaultCompanyId = company.Id,
            DefaultBranchId = headOffice.Id,
        };

        context.Users.Add(admin);

        // Assigned explicitly rather than relying on the super-user flag alone. The flag is an
        // emergency door; the assignment is what the permission screen shows, and an installation
        // whose only administrator is invisible there is one nobody can reason about.
        context.UserPermissionAssignments.Add(new UserPermissionAssignment
        {
            TenantId = tenant.Id,
            UserId = admin.Id,
            PermissionSetId = sets.Single(static s => s.Code == "ADMIN").Id,
            CompanyId = null,
            BranchId = null,
        });
    }

    private void SeedNumberSeries(
        Company company,
        List<Branch> branches,
        HashSet<string>? skip = null)
    {
        var yearStart = new DateOnly(DateTime.UtcNow.Year, 1, 1);

        // Gapless for anything a tax authority may ask about; gap-tolerant for internal
        // documents, which are far more numerous and have no such duty.
        Add("GJ", "General journal", allowGaps: true, "GJ-{YYYY}-00001");
        Add("SALES-QTE", "Sales quotes", allowGaps: true, "QT-{YYYY}-00001");
        Add("SALES-ORD", "Sales orders", allowGaps: true, "SO-{YYYY}-00001");
        Add("SALES-INV", "Sales invoices", allowGaps: false, "INV-{YYYY}-00001");
        Add("SALES-CM", "Sales credit memos", allowGaps: false, "SCM-{YYYY}-00001");
        Add("PURCH-REQ", "Purchase requisitions", allowGaps: true, "REQ-{YYYY}-00001");
        Add("PURCH-RFQ", "Quotation requests", allowGaps: true, "RFQ-{YYYY}-00001");
        Add("PURCH-ORD", "Purchase orders", allowGaps: true, "PO-{YYYY}-00001");
        Add("PURCH-INV", "Purchase invoices", allowGaps: true, "PINV-{YYYY}-00001");
        Add("PURCH-CM", "Purchase credit memos", allowGaps: true, "PCM-{YYYY}-00001");
        Add("TRANSFER", "Stock transfers", allowGaps: true, "TR-{YYYY}-00001");
        Add("COUNT", "Stock counts", allowGaps: true, "CNT-{YYYY}-0001");
        Add("BINMOVE", "Bin movements", allowGaps: true, "BM-{YYYY}-0001");
        Add("POS-SESS", "Till sessions", allowGaps: true, "Z-{YYYY}-00001");
        Add("EMP", "Employees", allowGaps: true, "EMP-0001");
        Add("PAYROLL", "Payroll runs", allowGaps: true, "PAY-{YYYY}-001");
        Add("LEAVE", "Leave requests", allowGaps: true, "LV-{YYYY}-0001");

        // Not year-dated: an employee keeps one number for as long as they work here, which a
        // year-reset series would not give them. Started well in the past rather than at
        // yearStart, because unlike a document a hiring date is routinely years old the day the
        // record is entered -- somebody moving onto ASAP brings their existing staff with them.
        Add("EMP", "Employee numbers", allowGaps: true, "EMP-00001", startingDate: new DateOnly(2000, 1, 1));

        // Gapless: a till receipt is a simplified tax invoice, and a sequence with holes in it is
        // a question from the authority. Branches that want their own may override the setting.
        Add("POS-RCP", "Point of sale receipts", allowGaps: false, "R-{YYYY}-000001");

        foreach (var branch in branches.Where(static b => b.Kind == BranchKind.Store))
        {
            // Receipts are numbered per branch, so two shops selling at once cannot collide and
            // a receipt number says on its face where it was issued.
            Add(
                $"POS-{branch.Code}",
                $"Point of sale receipts, {branch.Name}",
                allowGaps: false,
                $"{branch.Code}-{{YYYY}}-000001",
                branch.Id);
        }

        void Add(
            string code,
            string description,
            bool allowGaps,
            string startingNumber,
            Guid? branchId = null,
            DateOnly? startingDate = null)
        {
            // Already there, so leave it exactly as the company has it.
            if (skip?.Contains(code) == true)
            {
                return;
            }

            var series = new NumberSeries
            {
                TenantId = company.TenantId,
                CompanyId = company.Id,
                Code = code,
                Description = description,
                AllowGaps = allowGaps,
                BranchId = branchId,
                EnforceDateOrder = !allowGaps,
            };

            series.Lines.Add(new NumberSeriesLine
            {
                TenantId = company.TenantId,
                CompanyId = company.Id,
                NumberSeriesId = series.Id,
                StartingDate = startingDate ?? yearStart,
                StartingNumber = startingNumber,
                WarnWhenRemainingBelow = 500,
            });

            context.NumberSeries.Add(series);
        }
    }

    private void SeedDimensions(Company company)
    {
        var department = new Dimension
        {
            TenantId = company.TenantId,
            CompanyId = company.Id,
            Code = "DEPARTMENT",
            Name = "Department",
            NameArabic = "القسم",
            Description = "Which part of the business a transaction belongs to.",

            // Shortcut 1: copied onto every ledger entry, so filtering a million entries by
            // department is an index seek rather than a join through the set entries.
            ShortcutIndex = 1,
        };

        AddValues(department, [
            ("SALES", "Sales", "المبيعات"),
            ("OPS", "Operations", "العمليات"),
            ("ADMIN", "Administration", "الإدارة"),
            ("IT", "Information Technology", "تقنية المعلومات"),
        ]);

        var project = new Dimension
        {
            TenantId = company.TenantId,
            CompanyId = company.Id,
            Code = "PROJECT",
            Name = "Project",
            NameArabic = "المشروع",
            Description = "Optional project a transaction is attributed to.",
            ShortcutIndex = 2,
        };

        AddValues(project, [("GENERAL", "General", "عام")]);

        context.Dimensions.AddRange(department, project);

        void AddValues(Dimension dimension, (string Code, string Name, string Arabic)[] values)
        {
            foreach (var (code, name, arabic) in values)
            {
                dimension.Values.Add(new DimensionValue
                {
                    TenantId = company.TenantId,
                    CompanyId = company.Id,
                    DimensionId = dimension.Id,
                    Code = code,
                    Name = name,
                    NameArabic = arabic,
                });
            }
        }
    }

    /// <summary>
    /// Generates a password for the seeded administrator.
    /// </summary>
    /// <remarks>
    /// Generated rather than fixed, so a demo instance reachable from the network is not
    /// protected by a password published in the source. Shown once on the console and never
    /// stored in the clear.
    /// </remarks>
    private static string GeneratePassword()
    {
        // Ambiguous characters left out: this password gets read off a console and typed by hand,
        // and 0/O and 1/l/I cost more support calls than the entropy is worth.
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*";

        return RandomNumberGenerator.GetString(alphabet, 20);
    }
}
