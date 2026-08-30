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

- **done** The permission cycle end to end: the permission catalogue with each module's own
  sentence about what a permission does, permission sets you can assemble and edit, user accounts,
  and a password somebody must change the first time they use it. Two guards worth naming: nobody
  can turn off the account they are signed in with, and nothing may leave the installation without
  an account able to administer it. Proven with a purpose-built till operator whose menu is five
  entries where the administrator's is thirty-eight
- **done** The setup screen, generated from what the modules declare rather than written by hand

## Phase 1 — Finance

Everything else posts here, so it goes first.

- **done** Chart of accounts with account categories, types and totalling ranges
- **done** Fiscal years, periods, and the posting window that governs what may still be posted
- **done** Dimensions applied to accounts and enforced at posting. The axes and the validation
  existed already; what was missing was any way for a document to carry a value, so a company
  that made one mandatory would have had every posting refused. Documents now name them the way a
  person does — DEPARTMENT is SALES — and the document's analysis is overridden by a line's per
  dimension rather than wholesale, because a line naming a project must not lose the department
  the document set. Each mistake is its own refusal: a dimension that does not exist, one that
  has been retired, a value that does not exist and a value that is a heading or a total send
  somebody to four different places, and one message covering all of them sends them to none.
  Every bad code is reported at once. Sets are shared by fingerprint, and the lookup sees what
  the unit of work has already added, so a document whose lines share a combination creates one
  row rather than one per line. Shortcut dimensions are copied onto the entry, which is what
  makes grouping a million entries by department a seek rather than a join
- **done** General journals: batches, lines, reversal, and recurring journals. A recurring
  batch is a template that posts through the ordinary engine and then moves its own dates on, so
  nothing it produces is special — a recurring journal posting by its own route would be a second
  ledger with its own rules. Recurrences are date formulas rather than day counts: 1M+CM gives the
  31st, the 28th, the 31st and the 30th each in its turn and gets 29 February right, where thirty
  days on from 31 January is 2 March and lands an accrual on the second of the month twelve times
  a year. Amounts stay, clear after posting, or are read from the account's balance. A reversing
  line posts the opposite the following day, which is what an accrual is and the second half of
  what everybody forgets to do by hand. Dates move only after the posting succeeds, so a batch
  refused for a closed period is still due; the step is taken from the date it was due rather than
  from today, so a batch posted late does not walk its own schedule forward
- **done** The posting engine: balance validation, period checks, dimension checks, audit trail
- **done** General ledger entries, transaction grouping, running account balances
- *in progress* **Currencies and exchange rates, with realised gain and loss done** — rates are
  dated and only dated, so an invoice raised last March converts at last March's rate forever and
  entering today's does not restate it. Quoted as a pair rather than a multiplier, so 100 JPY =
  2.53 SAR is stated exactly instead of rounded into every line. A missing rate refuses the
  posting and names the currency and the day, because a rate that is guessed produces books that
  balance and are wrong, which nothing downstream will ever catch. Settlement of a foreign entry
  is decided in its own currency: a thousand dollars settles a thousand dollars whatever the two
  were worth in riyals, and the riyal residue is posted to exchange gain or loss rather than left
  on the control account or, worse, left looking like an unpaid balance. Unrealised revaluation
  of open foreign balances at a period end still to come, and until it exists a receivables
  figure is what the open invoices were worth on the days they were raised
- **done** Bank accounts and reconciliation. One ledger account per bank account, never
  shared, because two banks sharing one cannot be reconciled against either statement. The whole
  thing rests on one identity — the books are ahead of the bank by exactly the items the bank has
  not seen — checked at the moment of closing rather than at every keystroke, so the work can be
  done in any order and only the claim at the end has to be true. The check counts outstanding
  items against every statement ever agreed, not just the current one, so a line matched wrong
  last March surfaces at this month's close rather than sitting in the books. Matches are held on
  the statement line rather than on the entry, which leaves the ledger exactly as immutable as it
  claims to be. Amounts that differ warn rather than refuse, because one bank line covering three
  supplier payments is real; one entry settling two lines is refused, because the same money did
  not clear twice. Suggestions are offered only where there is exactly one candidate — two
  identical amounts in a week is a coin toss, not a decision. Importing a statement file, and
  payment files out, still to come
- **done** Customer and vendor ledgers with application and settlement, posted through the journal
  so the control account and the subsidiary ledger commit together
- **done** VAT/tax setup with dated rates, tax posting on journal lines, and a return-ready
  tax entry table the return is built from rather than from the tax account balance
- **done** Financial reports: trial balance, income statement, balance sheet, aged analysis and
  cash flow. The balance sheet carries the result for the year as its own computed line, so it
  balances before the year-end transfer exists. Cash flow ships as an account schedule rather
  than as code — every company's has a line the last one did not, and a version compiled into the
  product answers only the general case. Its ranges cover the whole chart, so its check row is
  nought by construction and a figure other than nought has found a real gap
- **done** Account schedules — the report builder that lets a user define statements without
  code. Rows name account ranges in the syntax the chart already uses, or add other rows up in a
  small formula language with brackets and the four operators. Rows are addressed by name rather
  than by position, so inserting one does not silently change every formula below it, and they
  are resolved by dependency rather than in order, so a statement may show its total at the top.
  A circle is refused and its rows named. The sign flip is applied before formulas run, which is
  the whole usability of the thing: somebody reads revenue and cost off the page and writes
  `R10 - R20`, and it means that. A figure with no answer — a margin on a month with no revenue —
  prints blank rather than nought and that blankness spreads to anything built on it, while a
  misspelt row name counts as nothing so one typo cannot blank a page. Column layouts comparing
  periods or a budget still to come
- **done** Year-end close: the result transferred to retained earnings on the year's last day,
  every income statement account cleared per branch so no shop keeps a balance the company total
  says is zero, and the year locked behind it. Refuses to run twice, refuses while an earlier year
  is still untransferred, and refuses on a year somebody locked before running it

## Phase 2 — Inventory

- **done** Items, units of measure and barcodes. Everything is stored in the base unit, because a
  stock figure with mixed units in it cannot be added up, so conversion happens once at the edge
  where somebody is standing at a till. A unit carries its own barcode and is looked for before the
  item's, which is what makes scanning a case of twelve add twelve rather than one. A screen sets
  both halves and answers "what does this barcode mean", refusing a barcode something else already
  carries -- two rows with one barcode makes a scan return whichever the database reached first,
  which nobody notices until a stock count. Variants, and categories as something a company can
  manage rather than an entity the seed fills in, still to come
- **done** Locations, and bins inside them. A bin is a refinement of a location, never a
  substitute: every stock figure, valuation and cost layer stays per location and the bin only says
  where inside. Costing per bin would mean one item at one location having two costs depending on
  which shelf it came off, so bins answer "where is it" and never "what is it worth" -- which is
  what makes turning them on at a live location safe. Off by default, because a shop with one
  stockroom does not need to name a shelf. On, every movement says which bin, softened only by a
  receiving bin for goods coming in; goods going out get no default, because guessing which shelf
  they came off is how a bin ends up holding stock nobody can find. A shelf short of what the
  location has is a warning naming the shelves that do have it, in pick order, rather than a refusal
  that would stop a picker holding the goods. Put-away and pick suggestions, bin capacity, and
  bin-to-bin movement as its own document still to come
- **done** Item ledger entries and value entries, quantity and cost tracked separately
- **done** Costing methods: FIFO, average, standard (specific to come)
- **Negative inventory**, allowed or blocked per company, with cost settled on later receipt so
  the cost layer never corrupts — **done**
- **done** Revaluation: what stock is worth changed without changing how much there is. The hard
  part is that it has to survive being sold -- a write-down posted as a lump sum against nothing in
  particular leaves the cost layers carrying their old figures, so the next sale is costed at the
  original purchase price and the inventory account drifts back with nothing erroring. So it writes
  against the open receipts themselves, per remaining unit, and every open receipt lands on the new
  figure whatever it was bought at. Only what is on hand: goods already sold have their cost of
  sales booked against the revenue they earned. The loss lands on variance rather than cost of
  sales, because nothing was sold. Revaluing a whole category at once, and lower-of-cost-or-market
  as a routine, still to come
- **done** Adjustment reasons. Without one every write-off is a bare negative adjustment and
  breakage, theft and expiry are indistinguishable -- same effect on quantity, almost nothing else
  in common, and one figure covering all three is a number nobody can act on. A reason carries the
  account, so somebody at the shelf says "breakage" and the cost reaches the breakage account
  without them knowing which one it is. Direction is checked because a reason used the wrong way
  round produces an entry that looks valid in every report that reads it, and a note can be demanded
  on the ones somebody will be asked about. Requiring a reason at all is a company setting, and an
  adjustment without one gets its own row in the shrinkage report rather than being dropped -- the
  gap would otherwise be exactly the entries nobody explained. A limit per reason still to come
- **done** Transfer orders, in-transit tracking, shipment and receipt including short receipts
  (transfer *requests* — a branch asking rather than being sent — still to come)
- Reorder policy, reservations, item availability
- **done** Inventory-to-finance posting through a kernel event, with expected cost held back
- *in progress* Reports: **stock on hand by location done**, with below-zero balances flagged and
  their estimated costs settleable from the screen; valuation, velocity, ageing to come
- **done** Physical stock count. The sheet freezes what the system said when it was made, so a
  sale rung up while somebody walks the aisles does not become a discrepancy nobody can explain.
  Nought and not-counted are different states and posting treats them differently: nought writes
  the stock off, uncounted is refused and, if overridden, left exactly as it was
- **done** Client screens: items, stock movements, stock on hand, transfers, stock counts

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
- **done** Selling in units other than the base one. Scanning a case of twelve rings one and
  stores twelve, priced at twelve singles, because a case price that was not simply twelve singles
  would be a second price on the same item. The receipt line keeps both figures and freezes what
  the case held, so redefining a case next year does not restate what somebody bought last year.
  Refused at the till rather than found later: half of something sold one at a time, and a unit
  the item has no conversion for
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
- **done** Branch performance reporting, consolidated at head office. The income statement cut
  by branch, reconciling to it exactly because it is built from the same entries. The work was not
  the report: it was that nothing outside payroll ever said which branch an entry belonged to, so
  every sale in the chain landed wherever the person who posted it happened to be signed in. A
  sale now posts at the till that rang it up, a purchase at the place the goods arrived, a stock
  movement at the location it moved in — per line, so a transfer between two shops has a side in
  each. What names none of them is shown on its own line rather than shared out

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
- **done** **Leave accrual and the leave register** — twenty-one days a year rising to thirty after
  five, accrued by the day rather than granted in a lump, with the rate changing partway through
  the year it changes in. Requests are made, granted, refused or withdrawn, and only a granted one
  comes off a balance or a wage. A request is refused if it runs backwards, falls outside when
  somebody actually worked here, or shares a day with one already on the books; asking for more
  than the balance warns rather than blocks, since a company sometimes agrees exactly that. Each
  kind of leave carries its own pay bands as data: sick leave at full pay for thirty days, three
  quarters for sixty and nothing for thirty after that, cumulative across the year rather than
  restarting at each absence — so a year of intermittent sickness costs exactly what one long
  absence costs. Attendance and shifts still to come
- **done** **End of service** — half a month a year for the first five and a full month
  thereafter, cumulative like tax bands rather than revalued at the final rate, and reduced by
  tenure on resignation. The bands are data, so another jurisdiction is a policy and not a fork
- **done** Payroll: what everybody is owed for a period, each wage split across the branches the
  days were worked at and posted one debit per branch, with end-of-service and deductions
  dividing the same way. Posting over days a posted run already paid is blocked and overridable
  with a reason, because a correction run is a real thing that does exactly that
- **done** Posting the provisions into Finance, with one writer each. Payroll charges
  end-of-service as it is earned, split across the branches the month was worked at, so a branch
  manager's staff cost includes what this month added to what will be owed. A provision run posts
  the leave side, which nothing else touches: it computes what the company owes today and posts
  however much that has moved since the last run, not the whole figure over again. Both go
  through the same kernel event every other module's posting does, so HR still knows nothing
  about Finance's tables — see
  [docs/architecture/module-dependencies.md](architecture/module-dependencies.md)
- Reconciling the end-of-service provision: payroll's charge accumulates month by month and
  nothing yet compares the running total against what the entitlement formula says it should be.
  Needs a way for a module to read a ledger balance without depending on Finance, which the
  kernel does not have yet
- A settlement posted at the moment somebody actually leaves, which is a different thing from
  either of the above
- **done** Employee, hiring, transfer, leaving and entitlement endpoints
- **done** Client screens: employees with their branch history, payroll with each line's split,
  and what the company owes
- Employee self-service
- **done** Reports: headcount and staff cost by where somebody is currently assigned, and
  turnover — opening and closing headcount, who joined and left in between, and the rate against
  the average of the two rather than against either end alone. Cost per branch is answered twice
  on purpose: this one is the contractual run rate on a day, branch performance in Phase 5 is
  what was actually posted over a period. Both sit behind the wage permission, since an aggregate
  is still a statement of what people are paid

## Phase 8 — Hardware stations

- **done** A station is a named set of devices bound to a branch and a till, which is what lets a
  shop be set up once and a broken till be swapped without anybody reconfiguring the software
- **done** Receipt and label printers, barcode scanners, cash drawers, customer displays, scales
  and payment terminals, each saying how it is reached. That is the distinction the record exists
  to make: a receipt printer goes through the browser's print dialog and a scanner types what it
  read, so most tills need nothing installed at all; a label printer or terminal is addressed over
  the network; and only the wired devices need a program on the till. Each till can be asked what
  it needs, and names the devices that make it so. A system that does not draw this line ends up
  answering "install our agent" to a shop that needed nothing
- **done** Print templates a user can edit without a developer. Three things in the language: a
  placeholder, a repeated region, and everything else printed exactly as written — spaces
  included, because a receipt is a fixed-width document and the spacing is the layout. Alignment
  is the same composite syntax .NET uses, so a column of figures can be made to line up. The
  editor previews against a real posted receipt rather than an invented one, and prints through
  the browser’s own dialog with no agent, no driver and no install
- **done** A local bridge agent so the browser can drive devices it otherwise cannot reach. It
  opens serial ports and sends bytes, holds no data and knows nothing about what is being sold,
  which is why whoever sets up the shop can install it rather than somebody with a password. It
  binds to the loopback interface and nothing else, not configurably, because a bridge reachable
  from the network is a cash drawer anybody on that network can open; and it answers only the
  pages named in its configuration. Every response carries the station code, and a request naming
  a different till is refused — two tills on one counter and a browser tab left open from
  yesterday both end with one till opening another's drawer otherwise. It simulates until told
  not to, and says so on every response, because a demonstration indistinguishable from the real
  thing is one somebody believes on a day it matters. The byte sequences and the reading of what
  a scale sends back are pure functions with eighteen tests against them: a drawer that opens on
  a real till and not on a test bench is a drawer nobody can debug

## Phase 9 — Extensibility and documentation

- **done** The extension SDK published as a NuGet package, with the kernel beside it — the
  kernel is the compatibility contract, so a package that could not restore it would make the
  contract untrue. Symbols and sources travel with both, so an author can step into ASAP's own
  code rather than guess at it. The SDK is more than a reference: a base class that turns a
  fourteen-member interface into the four members a first extension needs, helpers that build
  permission, setting and message keys so the prefix cannot be got wrong, and the conformance
  rules ASAP applies to its own modules shipped as one callable check
- **done** A worked sample extension, built end to end: permissions, a setting, bilingual
  messages with resolutions, a menu entry, a registered service, a manifest and its tests. Built
  in this repository against the SDK project rather than a published package, so it cannot go
  stale the first time the SDK changes. Its tests are the ones an author would write, starting
  with the one-line conformance check — and the check itself is proved failing as well as
  passing, because a check only ever run against something correct proves nothing
- **done** Generated developer reference: every event, message code, permission and setting ASAP
  declares, read from the same registries the running system uses rather than transcribed from
  them. It describes the installation in front of you, extensions included, which is the answer to
  "what can I integrate with" for somebody holding a deployment rather than a source tree
- **done** End-user guides per module, bilingual. Forty-one topics, one for every help topic a
  message or a setting points at, served in the reader's language and linked from the refusal
  itself. A conformance test refuses to let a message point at a topic that is missing, too short
  to be an explanation, or written in only one language — and refuses to let a topic exist that
  nothing points at, which is how a renamed message leaves its documentation behind
- **done** Upgrade and compatibility policy for extension authors, written down rather than
  implied: the kernel is the contract and nothing in it is removed or changes meaning within a
  major version; members may be added, which is why every optional member of the module interface
  has a default; everything outside the kernel may change between minor versions. An extension
  declaring one major version is refused on the next rather than loaded and hoped for, because
  one that half works writes half-correct figures into somebody's books and a wrong number that
  arrived quietly is worse than a system that would not start

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
