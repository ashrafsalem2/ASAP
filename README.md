# ASAP ERP

A modular ERP built so that each module can be sold on its own or as one suite.

**Stack:** ASP.NET Core 10 Web API · Angular 20 · SQL Server 2022 · EF Core 10

---

## What ASAP is

ASAP takes the ideas that work in Dynamics 365 Business Central, Odoo and SAP and puts them
behind one coherent platform:

| Idea | Where it comes from | How ASAP does it |
|---|---|---|
| Companies inside one installation | Business Central | Shared database, `CompanyId` on every row, enforced by query filters the module code cannot bypass |
| Extensions instead of forks | Business Central | Signed C# plugins in an isolated load context, subscribing to published events and adding fields through metadata |
| Modules sold and installed separately | Odoo | Every module declares its own permissions, settings, menu and schema; the host loads only what is licensed |
| Dimensions on every transaction | Business Central | A global dimension framework in the platform, used by finance, inventory, sales, purchasing and payroll alike |
| Posted entries are never edited | All three | Ledger entities carry no delete path at all; corrections are reversals, and both sides stay visible |

And adds the things this business actually needs:

- **Messages that explain themselves.** Every refusal answers what happened, why with the real
  numbers in it, and what to do next. A block that offers no way forward fails a startup check.
- **Negative stock without corrupting cost.** Selling into negative is a setup choice, and the
  costing engine settles the true cost when the goods arrive.
- **Offers that cannot quietly lose money.** The promotion engine prices against live cost and
  refuses a margin-destroying offer, naming the item and the shortfall.
- **Branches that sync both ways.** Head office and shop exchange data over a defined contract
  rather than an ad-hoc export.
- **Arabic and English, equally.** Not an English product with a translation file bolted on: every
  message, permission, setting and menu entry is declared in both languages at the point it is
  written, the client mirrors the right-to-left layout rather than reversing an English one, and
  server-rendered text follows the reader's language rather than the language they signed in with.

### On the two languages

This is a first-class requirement rather than a nicety, so it is enforced rather than trusted.
Bilingual support decays quietly — nobody decides to drop it; somebody adds one message in a
hurry, the English falls through as the Arabic fallback, and the gap stays invisible until a user
working in Arabic hits exactly that message, which for most of these is on the worst day they were
going to have anyway.

`tests/ASAP.Conformance.Tests` therefore holds the whole system to it: every message, permission,
setting and menu entry across every module must carry Arabic; the Arabic must not be the English
copied across, and must actually contain Arabic letters; the client's two dictionaries must define
the same keys; and a translated string must carry the same `{Placeholders}` as its English
original, because a sentence that loses `{No}` still reads as a sentence while no longer telling
anyone which transaction it means.

---

## Repository layout

```
ASAP/
├── src/
│   ├── Platform/
│   │   ├── ASAP.Platform.Kernel/          Contracts only, no dependencies. What extensions compile against.
│   │   ├── ASAP.Platform.Core/            Tenancy, security, setup, dimensions, number series, messaging
│   │   ├── ASAP.Platform.Persistence/     EF Core, company query filters, module schema registration
│   │   └── ASAP.Platform.Extensibility/   Plugin loading, event binding, metadata field extensions
│   ├── Modules/
│   │   └── ASAP.Modules.Finance/          Chart of accounts, journals, general ledger, posting, reports
│   ├── Sdk/
│   │   └── ASAP.Extensions.Sdk/           The NuGet surface third-party developers build against
│   └── ASAP.Api/                          HTTP host; composes the platform with the licensed modules
├── tests/
├── frontend/                              Angular 20 workspace
└── docs/
    ├── architecture/                      How ASAP is put together, and why
    ├── developer/                         Building modules and extensions
    └── user/                              End-user guides
```

The dependency rule is one way and never bent: `Kernel` knows nothing, `Core` knows `Kernel`,
`Persistence` knows `Core`, modules know `Persistence`, and the API host knows the modules. A
module never references another module directly — it talks to one through published events and
the contracts in `Kernel`, which is what allows a customer to buy Inventory without Sales.

---

## Getting started

Requires the .NET 10 SDK, Node 20 or later, and SQL Server or LocalDB.

The signing key is never committed. Set one before the first run — startup refuses a key that is
missing, short, or still the placeholder, because a guessable one lets anyone mint a token for any
user in any company:

```bash
cd src/ASAP.Api && dotnet user-secrets set "Asap:Jwt:SigningKey" "$(openssl rand -base64 48)"
```

Then run it. Migrations apply automatically, and an empty database is seeded with a demo company:

```bash
cd src/ASAP.Api && dotnet run
```

On first run the console prints a generated password for the `admin` account. It is shown once
and cannot be recovered — the hash is one-way by design — so copy it before the window scrolls.

- API: `http://localhost:5199`
- Interactive API reference: `http://localhost:5199/scalar/v1`
- Health: `http://localhost:5199/health`

The demo seed creates one tenant, one company (`MAIN`, SAR), three branches (head office plus
Riyadh and Jeddah stores), three permission sets, seven number series and two dimensions.

```bash
dotnet test ASAP.slnx
```

Further setup steps are documented in [docs/developer](docs/developer) as each layer lands.

---

## Status

Under active construction. See [docs/ROADMAP.md](docs/ROADMAP.md) for what is built, what is
next, and in what order.
