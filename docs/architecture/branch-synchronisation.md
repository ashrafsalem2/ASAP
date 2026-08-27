# Branch synchronisation

A shop cannot stop selling because the line to head office is down, and head office cannot run a
company whose branches each hold a different idea of what things cost. Those two sentences pull in
opposite directions, and everything here is the compromise between them.

## The rule that makes this tractable

**Every row has exactly one writer.**

- **Master data flows down.** Items, prices, customers, tax codes, permission sets and setup
  values are written at head office and never at a branch. A branch holds a copy and reads it.
- **Transactions flow up.** Receipts, sessions, stock movements and their ledger entries are
  written at the branch that made them and never at head office.

That asymmetry is not a convention, it is the whole design. Two-way synchronisation between
systems that may both edit the same row is a problem with no correct answer — only a choice of
which edit to lose, dressed up as a merge strategy. Give every row one writer and the question
stops being asked.

The cases people expect to be conflicts turn out not to be:

| Feared conflict | Why it cannot happen |
|---|---|
| Two branches issue the same receipt number | Number series are per branch. `JED-01-2026-000123` says on its face where it was issued. |
| Head office and a branch both edit an item | A branch has no write path to master data. The attempt is refused, not merged. |
| A branch posts against a price head office has since changed | The branch posts what it charged. Master data is a copy that moves forward; a posted document is a fact that does not. |
| The same receipt is pushed twice | Every pushed document carries an idempotency key. A replay is accepted and ignored. |

What remains is one real case, handled explicitly:

**A branch sells something head office has not told it about yet.** This is possible whenever a
branch has been offline: an item created this morning, sold this afternoon at a shop that last
synchronised yesterday. The document is accepted and held rather than refused, and reported, so
somebody sees the gap instead of a cashier being told no at a counter for a reason they cannot
act on.

## The feed

Changes to master data are captured where every other cross-cutting concern is captured — in
`AsapDbContext.SaveChanges`, not in each module. A module that had to remember to publish its own
changes would one day forget, and the failure would be a branch quietly running on stale prices.

Each change becomes one `SyncChange` row carrying a monotonic `Sequence`, the entity type, its
key, and the operation. The sequence is the cursor: a branch asks for everything after the last
number it kept, applies it in order, and stores the new number. That is the entire protocol on
the way down.

Three properties matter and are tested:

- **Ordered.** A branch applies changes in sequence order, so an item that was created and then
  blocked is never applied the other way round.
- **Resumable.** The cursor lives at the branch. A branch that has been off for a week asks from
  where it left off, and nothing at head office has to remember which branches exist.
- **Idempotent.** Applying the same change twice is applying an upsert twice. A branch that
  crashes mid-apply re-applies and lands in the same place.

## The way up

A branch pushes documents it has already posted locally. Head office does not re-decide them —
the sale happened, the money was taken, the stock left a shelf in another city. What head office
does is record them, once.

Once is enforced by the idempotency key rather than by the document number, because the two
answer different questions. A document number says which document this is. An idempotency key
says which *attempt* this is, and it is what lets a till that lost its connection mid-post retry
without anybody having to work out whether the first attempt landed.

The key is supplied by the caller and is unique per branch. A push carrying a key already in the
inbox returns the original outcome rather than posting again — the same answer the caller would
have got if the first attempt's response had not been lost, which is the point.

## What this does not do yet

- Master data is captured and served; a branch-side applier that consumes the feed into a second
  database is not written, because there is not yet a second database to write it into.
- Pushed documents are recorded and deduplicated; the per-document-type handlers that replay
  them into head office ledgers are declared and not yet implemented for every type.
- There is no conflict *reporting* screen. The rules above mean there is little to report, but
  "the branch sold an item we have not sent it" deserves somewhere to appear.

These are noted because a synchronisation design that overstates what it covers is worse than one
that covers less: the first is discovered during a month-end close.
