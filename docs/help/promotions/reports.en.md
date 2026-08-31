# Offer reports

What each offer actually did, and what moved while it ran.

## Where the figures come from

Promotions decides what an offer does and refuses the ones that would sell below cost. It has no idea what happened afterwards, because the documents an offer lands on belong to whichever module sold the goods — and those modules depend on Promotions rather than the other way about.

So it asks. Every module that sells under an offer answers for its own documents, which means a till sale and any other door are the same rows here. A report built any other way would either reach across the module graph backwards or quietly cover one door out of several.

## The margin is the same number the refusal used

Cost comes from **what the costing engine said at the moment of sale** — the same figure the margin floor was checked against when the offer was allowed to apply.

That is deliberate. A report and a refusal reading different numbers would eventually disagree about the same offer, and the report is the one nobody can argue with afterwards.

### Lines with no cost

Some lines carry no recorded cost: a receipt written before the column existed, or a charge line with no goods behind it. Those are **left out of the margin and counted separately**, in their own column.

Treating a missing cost as nought would report a hundred per cent margin on every line nobody has a figure for — a confident answer produced entirely by missing data. The separate count is there so you can see how much of the offer the margin actually covers.

### No percentage where there is nothing to divide by

An offer that gave the goods away has a real negative margin and no percentage at all. Printing nought would say the opposite of the truth, and it is the sort of nought a spreadsheet then averages.

## Offers nobody used

They appear, with zeros and a flag.

An offer nobody used is the most useful row in the report. One that listed only what worked could not tell you which campaign was a waste of a fortnight, which is usually the question being asked.

## What moved — a comparison, not a cause

The second table shows how much of the offer's items sold during it, against the same length of time immediately before.

**This is a comparison and nothing stronger.** Sales move for a season, a competitor, the weather, a shelf that happened to be empty in the earlier window, a payday falling differently — and none of that is visible here.

There is deliberately no cannibalisation percentage. A figure computed from these two numbers would be inventing a cause out of a coincidence, and it would be believed precisely because it had a decimal point in it.

What it is good for is *noticing*. An item that sold no more under an offer than it did without one is worth a question, and the question is the output.

## Seeing what an offer would do before it runs

That is on the offer screen rather than here, and it is the same arithmetic that refuses an offer breaking the margin floor — deliberately the same code path, because a preview that disagreed with the refusal would be worse than no preview at all.

It matters because an offer is written weeks ahead against costs that were true then, and suppliers put prices up.

## What you need to be allowed to run them

`Promotions.Offer.Read`.
