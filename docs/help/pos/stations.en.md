# Tills

A till is a named point of sale bound to a branch and a location. Everything a sale needs to know about where it happened comes from the till.

## What a till decides

**Which branch the sale belongs to.** This is what puts the day's takings in the right shop's figures. A till with no branch reports its sales wherever the person posting happened to be signed in, which for anything running at head office is the whole chain's revenue at head office.

**Which location stock leaves from.** The shelves the goods were actually taken off.

**Which customer a cash sale is against.** A default customer, because a cash sale names nobody and the accounting still needs a party.

**What discount a cashier may give without asking.** Above that, a refusal that somebody with the permission can push past, with a reason.

## The shelf a till sells off

Only where the till's location tracks bins — most shop floors do not, and there is nothing to set.

Where it does, somebody has to say which bin the shop floor is. Not because the system cannot guess, but because guessing is exactly what the bin rules refuse: goods leaving a bin-tracked location must name the shelf they came off, or a bin ends up holding stock nobody can find.

A cashier cannot answer that. They took the goods off the shop floor; which shelf that is on the warehouse map is a fact about the building. So it is asked once, in advance, and every sale at that till uses it. Nothing is guessed — somebody wrote it down.

A till at a bin-tracked location with no shelf set cannot sell, and says so in those terms. The alternative was letting the sale reach Inventory and fail with a message telling the reader to name a bin, which is sound advice on a warehouse journal and useless to somebody with a queue and no field to type it in.

## Receipt numbering

Receipts are numbered per till's branch, so two shops selling at the same moment cannot collide, and a receipt number says on its face where it was issued. Opening a branch creates its series automatically.

The series is gapless. A till receipt is a simplified tax invoice, and a sequence with holes in it is a question from the authority.
