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

- **done** Till sessions with an opening float, X and Z readings, cash declaration and the
  variance posted rather than argued about. Card takings are excluded from the drawer, which is
  the mistake that otherwise leaves every till short by the day's card sales
- **done** Sales, returns, exchanges, park and recall, split and mixed tender. A return refunds
  at the price paid, discount and all, and is counted against every earlier return on that
  receipt — checking only the transaction in hand lets somebody return two, then two more,
  against a sale of two
- **done** A receipt posts exactly as a sales invoice does: revenue at list with the discount as
  a contra, tax on both, stock out at what the goods cost. The P&L cannot tell which door a sale
  came through
- *in progress* Offline-first till: the idempotent push a queued till reconciles through is
  built and tested; the local queue and the branch-side applier need the split deployment
- *in progress* **Two-way branch synchronisation** — the contract, the ordered resumable change
  feed, the idempotent push and the per-branch cursor are done and documented in
  docs/architecture/branch-synchronisation.md. Every row has exactly one writer, which is what
  makes the conflict rules short enough to state. The branch-side applier that consumes the feed
  into a second database waits on there being a second database
- **done** Client screens: the till, and every session with what its drawer came to
- Branch performance reporting, consolidated at head office

## Phase 6 — Promotions

- **done** Offer types: percentage, amount per unit, buy-X-get-Y, threshold and fixed price.
  Happy hour and coupon are windows and conditions on those rather than kinds of their own, which
  is why a half-price-second-one needs no new code. Bundles across different items, and loyalty,
  are still to come
- **done** Eligibility by branch, channel, coupon, date window, time-of-day window and day of the
  week. A window that ends before it starts crosses midnight, which is how a late-night offer is
  written and would otherwise switch every one of them off. Customer group is modelled and needs
  groups on the customer to be useful
- **done** **Margin protection**, priced against live cost both when the offer is saved and again
  at every basket — because a campaign is planned weeks ahead and suppliers put prices up. The
  message names the item, its cost today, the offer price and the shortfall per unit. It refuses
  where somebody can act on it, and warns where they cannot: a shop must not stop selling water
  because a promotion on it was misconfigured last week
- **done** Stacking and priority. The customer gets the best offer and priority is only a
  tiebreak; an exclusive offer must beat what the stackable ones would have come to together; a
  blocking offer is settled before anything is worked out per line
- **done** The discount posts to its own account, separate from the ordinary sales discount, and
  the receipt line carries the offer code — so a campaign can be totalled either way
- Reports: offer uptake, realised margin, cannibalisation
- Client screens: the offer list, and the margin preview that shows what an offer would do before
  it runs

## Phase 7 — Human resources

- **done** Employees and positions. Contracts as a separate versioned document still to come
- **done** Branch assignment as an effective-dated history rather than a column, so payroll can
  split a month between two branches on the day somebody transferred. A column would charge the
  whole month wherever they happened to be on payday, and the branch they left would look cheaper
  than it was every time anybody moved
- Head-office hiring, onboarding and offboarding
- *in progress* **Leave accrual and the leave register done** — twenty-one days a year rising to
  thirty after five, accrued by the day rather than granted in a lump, with the rate changing
  partway through the year it changes in. A record is refused if it runs backwards, falls outside
  when somebody actually worked here, or shares a day with one already on the books; taking more
  than the balance warns rather than blocks, since a company sometimes agrees exactly that.
  Attendance and shifts still to come
- *in progress* **End of service done** — half a month a year for the first five and a full month
  thereafter, cumulative like tax bands rather than revalued at the final rate, and reduced by
  tenure on resignation. The bands are data, so another jurisdiction is a policy and not a fork.
  Earnings and deductions — an actual payroll run — still to come
- **done** Posting the two provisions into Finance: a run computes what the company owes today in
  end-of-service and unused leave and posts however much that has moved since the last run, not
  the whole figure over again. Goes through the same kernel event every other module's posting
  does, so HR still knows nothing about Finance's tables — see
  [docs/architecture/module-dependencies.md](architecture/module-dependencies.md). A settlement
  posted at the moment somebody actually leaves is a different thing and still to come
- **done** Employee, hiring, transfer, leaving and entitlement endpoints — HR had no API surface
  at all until now
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
