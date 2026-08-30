# Adjustment reasons

Why stock was written off, and which account the loss lands in.

## One number for three problems answers none of them

Without a reason, every write-off is a bare negative adjustment and breakage, theft and expiry are
indistinguishable. They have the same effect on quantity and almost nothing else in common:
breakage is a warehouse conversation, theft is a security one, expiry is a buying one.

A single shrinkage figure covering all three is a number nobody can act on. Splitting it is the
whole point of the list.

## A reason carries the account

That is what makes it more than a label. Somebody at the shelf chooses "breakage" and the cost
reaches the breakage account without them having to know which one that is.

The shipped list points at accounts shipped with it:

| Reason | Lands in |
| --- | --- |
| `COUNT` — stock count difference | Inventory adjustment |
| `BREAKAGE` — broken or damaged | Stock breakage |
| `THEFT` — missing or stolen | Stock shrinkage |
| `EXPIRY` — past its date | Stock expiry |
| `SAMPLE` — given away | Samples and donations |
| `FOUND` — found on the shelf | Inventory adjustment |

A reason with no account named falls back to the item category's variance account, so a company can
add one without touching its chart of accounts.

## Direction

Breakage cannot increase stock. Goods found cannot decrease it. A count difference genuinely goes
either way, which is why it is the only one left open.

The check is worth having because getting it wrong is invisible. Breakage recorded as an increase
produces an entry that looks perfectly valid in every report that reads it — the quantity is right,
the account is right, and the only thing wrong is that the company appears to have gained stock by
breaking it.

## Notes

A reason can demand that somebody writes something as well as choosing it. `THEFT` does; nothing
else in the shipped list does.

Everything else there is an accident. Theft is the one somebody will be asked about, and a row with
nothing written against it is a row that has to be reconstructed from memory months afterwards.

## Whether a reason is required at all

Off by default, and a company setting rather than a rule. A corner shop writing off a broken bottle
should not have to maintain a code list; a chain that cannot say what its shrinkage was made of
should.

With it off, an adjustment without a reason still posts — and appears in the shrinkage report under
a row of its own. It is not dropped. A report that quietly omitted unexplained adjustments would
understate the total, and the gap between it and the ledger would be exactly the entries nobody
explained, which is the last thing a shrinkage report should hide.

## Withdrawing one

Withdrawn, not deleted. Entries already posted against a reason still point at it, and a report
covering last year has to be able to name it.

## What is not here yet

**A limit per reason.** Nothing stops a hundred thousand riyals being written off as breakage
without a second signature.

**Reasons on transfers and returns.** Only adjustments are asked. A sale, a purchase and a transfer
already say why they happened — the document behind them is the reason — and demanding a code as
well would be asking the same question twice.

**Reason on a stock count's own adjustment.** A count posts its differences under `COUNT` today
whatever caused them; splitting a count's variances by cause needs the counter to say line by line.
