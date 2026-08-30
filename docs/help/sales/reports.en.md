# Sales reports

What was sold, what it cost, and what is still to go out.

## Margin comes from the item ledger

Not from sales documents. Every outbound value entry carries what the goods sold for alongside what
they cost, which is why a margin can be read without going back to an invoice.

One consequence is worth stating plainly: **a sale at a till and a sale on an invoice are the same
rows here.** The margin report cannot tell which door a sale came through, any more than the profit
and loss can. That is the point of it.

## The column that matters most

**Estimated cost.** A sale made from stock that had not arrived yet is valued at an estimate, and
its margin stays provisional until the goods are received and the settlement runs.

A report that presented that as a fact would give somebody a number to act on that then changes
underneath them. So every row says how much of its cost is still in doubt. A row whose estimated
cost is most of its cost is not telling you about your margin; it is telling you to come back after
the goods arrive.

## Thinnest margin first

A margin report is read to find the problems. The items making money need no attention, and sorting
them to the top would bury the ones losing it.

## A percentage with no answer stays blank

Goods given away have a real, negative margin and no margin percentage — the division is by nought.
Printing that as nought would be a lie, and a lie a spreadsheet then averages into everything else.

## Returns are netted off the month they came back in

A month with a lot of goods coming back reports the margin the company actually made, not the one it
made before anybody changed their mind. A return carries a negative quantity and negative revenue,
and both belong in the figure.

## By customer, across every channel

Sales cannot see the till and the till cannot see Sales — neither module references the other, by
design. So each answers for its own documents and the report takes both. A company running only one
of them gets a report covering exactly the channel it has.

Till sales appear under the station's walk-in customer, because that is what the till recorded them
against, and a shop doing most of its trade over a counter will find one enormous row there. Nothing
is wrong with it: that is genuinely who bought the goods, as far as anybody knows.

A sale whose document nothing recognises is left out rather than gathered under a blank customer. It
means the module that owns that document is not installed, and inventing a row for it would put
somebody else's trade in this company's worst-margin list.

## Open orders

What is ordered and has not shipped, most overdue first. Overdue is measured against the delivery
date the order asked for; an order that never named one shows a blank rather than a nought, because
nobody is late when nobody said when.

## What is not here yet

**Margin after promotions.** An offer applied at a till reduces revenue on the line, so it is in
these figures — but nothing separates "margin we planned" from "margin after discounting", which is
the question a promotion post-mortem actually asks.

**Margin by branch or by salesperson.** Both are on the entries; neither is grouped by yet.

**A period comparison.** One range at a time, so "is this worse than last quarter" is two runs and a
subtraction done by hand.
