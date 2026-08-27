# ASAP ERP — build roadmap

Order matters here. Finance is built first because every other module posts into it; inventory
before purchasing and sales because both move stock; point of sale after sales because a till is
a sales document with a cash drawer attached. Nothing is built on a foundation that is not
already carrying weight.

Legend: **done** · *in progress* · planned

---

## Phase 0 — Platform foundation

The part every module stands on. No business logic lives here.

| Area | Contents | Status |
|---|---|---|
| Kernel contracts | Entity bases, tenancy scoping, audit, soft delete, concurrency | **done** |
| Messaging | Message codes, catalogue, severities, blocked-with-resolution rule | **done** |
| Results | `Result` / `Result<T>` carrying messages instead of throwing for business outcomes | **done** |
| Events | Domain, integration and vetoable events; handler and publisher contracts | **done** |
| Modules | Module contract, navigation, permissions, setup and message declaration | **done** |
| Security contracts | Permission descriptors, nine fixed verbs, per-company user context | **done** |
| Setup contracts | Typed setup descriptors, four scopes, narrowest-wins resolution | **done** |
| CQRS | Request/handler/pipeline contracts, declarative permission attribute | **done** |
| Platform entities | Tenant, company, branch, user, permission set, setup value, audit log, outbox | **done** |
| Number series | Gapless or gap-tolerant per series, dated ranges, per-branch numbering | **done** |
| | *This read "done" for a while on the strength of the contract, entities, seed and admin screen. Nothing implemented the service that issues a number, and the gap only surfaced when Transfers asked for one. Now genuinely done, and tested.* | |
| Dimensions | Canonical combinations, shared dimension sets, shortcut dimensions | **done** |
| Persistence | DbContext, company query filters, module schema registration, migrations | **done** |
| Module runtime | Dependency ordering, cycle detection, per-tenant licence gating | **done** |
| Event bus | Handler dispatch and ordering, vetoable events, transactional outbox | **done** |
| Extension host | Isolated plugin loading, manifest gate, signature check | **done** |
| Metadata extensions | Adding fields to core tables from an extension | planned |
| CQRS pipeline | Dispatcher, declarative permissions, startup permission audit, transactions | **done** |
| API host | JWT auth with rotating refresh tokens, problem details carrying ASAP messages | **done** |
| Demo seed | Tenant, company, branches, permission sets, number series, dimensions | **done** |
| Setup service | Reading and writing declared settings, narrowest-scope-wins | **done** |
| Angular shell | Layout, auth, company switcher, generated menu, bilingual AR/EN with RTL | **done** |

## Phase 1 — Finance

Everything else posts here, so it goes first.

- **done** Chart of accounts with account categories, types and totalling ranges
- **done** Fiscal years, periods, and the posting window that governs what may still be posted
- Dimensions applied to accounts and enforced by dimension posting rules
- *in progress* General journals: batches and lines done, **reversal done**; recurring journals to come
- **done** The posting engine: balance validation, period checks, dimension checks, audit trail
- **done** General ledger entries, transaction grouping, running account balances
- Currencies and exchange rates, with realised and unrealised gain/loss
- Bank accounts and reconciliation
- **done** Customer and vendor ledgers with application and settlement, posted through the journal
  so the control account and the subsidiary ledger commit together
- **done** VAT/tax setup with dated rates, tax posting on journal lines, and a return-ready
  tax entry table the return is built from rather than from the tax account balance
- *in progress* Financial reports: **trial balance, income statement, balance sheet and aged
  analysis done**; cash flow to come. The balance sheet carries the result for the year as its own
  computed line, so it balances before the year-end transfer exists
- Account schedules — the report builder that lets a user define statements without code
- Year-end closing and retained earnings transfer

## Phase 2 — Inventory

- Items, variants, units of measure, item categories, barcodes
- *in progress* Locations done; warehouses and bins to come
- **done** Item ledger entries and value entries, quantity and cost tracked separately
- **done** Costing methods: FIFO, average, standard (specific to come)
- **Negative inventory**, allowed or blocked per company, with cost settled on later receipt so
  the cost layer never corrupts — **done**
- Adjustments, revaluation, physical count
- **done** Transfer orders, in-transit tracking, shipment and receipt including short receipts
  (transfer *requests* — a branch asking rather than being sent — still to come)
- Reorder policy, reservations, item availability
- **done** Inventory-to-finance posting through a kernel event, with expected cost held back
- *in progress* Reports: **stock on hand by location done**, with below-zero balances flagged and
  their estimated costs settleable from the screen; valuation, velocity, ageing to come
- **done** Client screens: items, stock movements, stock on hand, transfers

## Phase 3 — Purchasing

- **done** Vendors (through the Finance party ledger); purchase prices, discounts and lead times to come
- *in progress* **Purchase order, goods receipt and vendor invoice done**, with the three-way match
  between them; requisition, request for quotation, return and credit memo to come
- Approval limits and the approval workflow
- Landed cost applied across an item charge
- **done** Posting into inventory and the vendor ledger, through a goods-received-not-invoiced
  accrual so the company owes for stock from the day it lands rather than the day the post does
- **done** Client screens: purchase orders, goods receipt and vendor invoice
- Reports: purchase analysis, vendor performance, open orders

## Phase 4 — Sales

- **done** Customers, credit limits and payment terms (through the Finance party ledger); a
  line discount held as a percentage so what was given away stays reportable. Price lists and
  discount structures to come
- *in progress* **Order, shipment and invoice done**; quote, return and credit memo to come
- **done** Availability decided at shipment, by the costing engine, which is the only thing that
  knows what is on the shelf at the moment somebody reaches for it. Reservation to come
- **done** Posting into inventory, cost of sales and the customer ledger. A shipment moves stock
  at what the goods cost and an invoice bills what was agreed, and the two never meet — which is
  the only way the margin report describes anything
- **done** Revenue posted at list with the discount as a contra, each carrying the tax code, so
  the tax lands on what the customer actually pays and the discount stays visible in the P&L
- **done** The location is asked about when the order is taken, not at the despatch bay
- **done** Client screens: sales orders, shipment and invoice
- Reports: sales analysis, margin by item and customer, open orders

## Phase 5 — Point of sale and branch operations

- Till sessions, opening float, X and Z readings, cash declaration and variance
- Sales, returns, exchanges, park and recall, split and mixed tender
- Offline-first till that queues and reconciles when the link returns
- **Two-way branch synchronisation** over an explicit contract: master data down from head
  office, transactions up from the branch, with conflict rules stated rather than implied
- Branch performance reporting, consolidated at head office

## Phase 6 — Promotions

- Offer types: percentage, amount, buy-X-get-Y, bundle, threshold, happy hour, coupon, loyalty
- Eligibility by customer group, branch, channel, date and time window
- **Margin protection**: an offer is priced against live cost and refused when it would sell
  below the configured floor, naming the item, its cost, the offer price and the shortfall
- Stacking rules and priority when several offers could apply
- Posting the discount to its own account so the cost of promotion is visible in the P&L
- Reports: offer uptake, realised margin, cannibalisation

## Phase 7 — Human resources

- Employees, contracts, positions, org structure
- Branch assignment and transfer between branches, with the effective date driving payroll split
- Head-office hiring, onboarding and offboarding
- Attendance, shifts, leave types, balances and accrual
- Payroll: earnings, deductions, end-of-service, and posting into finance
- Employee self-service
- Reports: headcount, turnover, cost per branch, leave liability

## Phase 8 — Hardware stations

- A station is a named set of devices bound to a branch and a till
- Receipt and label printers, barcode scanners, cash drawers, customer displays, scales
- Print templates that a user can edit without a developer
- A local bridge agent so the browser can drive devices it otherwise cannot reach

## Phase 9 — Extensibility and documentation

- The extension SDK published as a NuGet package
- A worked sample extension, built end to end
- Generated developer reference: every event, message code, permission and setting ASAP declares
- End-user guides per module, bilingual
- Upgrade and compatibility policy for extension authors

---

## Ground rules

These hold across every phase.

1. **Posted entries are immutable.** Corrections are reversals. No module gets a delete path
   into a ledger.
2. **Every refusal explains itself.** What happened, why with the real numbers, what to do next.
   A blocking message with no resolution fails a startup check.
3. **Cost integrity is never traded for convenience.** Negative stock is permitted where the
   business wants it, but the costing engine settles the true cost afterwards regardless.
4. **A module never references another module.** They meet through events and kernel contracts,
   which is what lets each one be sold on its own.
5. **Nothing is configured in a place the setup screen cannot see.** If it is a setting, it is a
   declared `SetupDescriptor`.
