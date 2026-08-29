# Statement layouts

The shipped income statement and balance sheet answer the questions everybody has. What they cannot answer is the question *your* company has — the one where marketing is split out of overheads, or where a covenant is measured on a figure the bank defined.

Those questions are endless and specific. A system that needs a developer for each one has effectively said no to all of them, so statements here are data you can edit.

## How a layout is built

Rows, in the order they print. Each row is one of three things:

**Accounts.** The row shows what a set of accounts came to. Write the set the way the chart of accounts already writes its totals:

- `4000..4999` — everything from one to the other, both ends included
- `6100|6110` — just those two
- `6000..` — everything from there on
- `4000..4999|4900` — both, and 4900 is still counted once

**Formula.** The row adds other rows up: `R30 - R50 - R60`. Rows are named, not numbered by position, so inserting a row between two others does not silently change what every formula below it means. Number yours in tens so there is room to insert.

Formulas take `+ - * /` and brackets. Multiplication and division bind tighter, so `R10 + R20 / 2` halves R20 and then adds — which is what everybody who writes it means. `R30 / R10 * 100` is a margin.

**Heading.** No figure. A title, or a blank line for the eye.

## Signs, which is where statements go wrong

Revenue is a credit, which the ledger holds as a negative number. Printing sales as negative makes a reader distrust every other figure on the page, so each row has a switch that turns its sign.

**The switch is applied before formulas run.** So a formula means what it looks like it means: you read revenue and cost off the statement in front of you and write `R10 - R20`. If formulas worked on the ledger's own signs, that subtraction would quietly become an addition and nothing on screen would explain why.

The rule that follows: turn the sign on rows whose natural direction is a credit — revenue, other income, liabilities — and leave costs alone, because a debit is already positive. Then write formulas exactly as a printed statement reads.

## Rows may be in any order

Rows are worked out by what they depend on, not by where they sit. A statement that shows its total at the top works exactly as well as one that shows it at the bottom.

What is refused is a circle: a row that adds up a row that adds up the first has no starting point. The rows in the circle are named, so you can follow the formulas until you find the one pointing at itself.

## Figures that have no answer

A margin on a month with no revenue prints blank, not nought. Nought per cent is a claim, and it is not a true one about a month that had no sales.

That blankness spreads: a total built on a figure nobody could work out is not a figure either. A misspelt row name is different — it counts as nothing and the statement still prints, because one typo should not blank the page.

## The shipped layouts

Two, and they are meant to be copied.

**Profit and loss** shows how a subtotal is written, how a sign is turned, and how a percentage row works.

**Cash flow** is the more interesting one. It uses the indirect method: start with the result, then adjust for everything that moved without being cash — what customers owe, what is in stock, what the company owes, what went into fixed assets.

Its ranges cover the whole chart on purpose. Every account is in exactly one of them, so the last row is nought *by construction* rather than by luck: every entry balances, so what cash did must be the mirror of what everything else did.

**If that check row is not nought, it has found something real** — almost always an account added to the chart after the statement was written, sitting in no range. Widen a range and it comes back to nought.

The classic add-back of depreciation is not a separate line, and that is the price of the exactness. Depreciation's other side lands in accumulated depreciation, which is already inside "tied up in fixed and other assets"; adding it again counts it twice and puts the check row out by exactly the depreciation charge. To break the line out, split the fixed asset row into two rows whose ranges do not overlap — one for accumulated depreciation, one for the rest — and the check still holds.

## Balances or movements

Each row says which it wants. A profit and loss row wants **what moved** between the two dates. A balance sheet row wants **what the balance stood at** on the closing date. Mixing the two on one statement is fine and is exactly what a cash flow does.
