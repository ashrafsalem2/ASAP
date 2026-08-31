# Valuation, ageing and velocity

Three questions about the same stock, answered from the same place.

All of them are read from the **cost ledger** rather than from the running figures on the item. `Item.UnitCost` is a convenience, kept up to date so the next posting has something to work with; the ledger is the record. Only the ledger can be asked what something was worth on a date that has already gone by, and that is the only kind of question a period end ever asks.

## Valuation

Every cost amount posted on or before the date, grouped by item, variant and location.

That is deliberately **the same arithmetic that posts to the inventory account**. The valuation and the balance sheet tie by construction rather than by agreement — they are built from the same rows. A valuation worked out any other way is a second opinion, and a second opinion about the inventory account is exactly what nobody wants at a period end.

Revaluations are included, because a revaluation is a value entry carrying a cost and no quantity. Landed cost that arrived after the goods is included for the same reason.

### The unsettled column

Some of the value may be a guess. When goods are sold before they arrive, the cost of sale is valued at an estimate and the entry is marked as such; the settlement routine corrects it when the real cost turns up.

That column measures **exposure, not a slice of the total**. It says how much value rests on costs nobody has confirmed. On a location that has gone negative it can be larger than the value beside it, which is not a contradiction: almost all of a negative balance can be guesswork.

It is telling you something specific: come back after the settlement runs. Do not reconcile around it and do not treat it as an error. It is a real figure and it will move.

### Quantity of nought with a value

A location holding no stock and some value is a real state, not a bug. It is goods sold before they arrived: the quantity has netted out and a balance is waiting to be settled. There is no unit cost for such a row, so none is shown rather than a division by nought.

## Ageing

How long the stock on hand has actually been on hand, in bands of 0–30, 31–60, 61–90, 91–180 and over 180 days.

It is read straight off the cost layers, which is the one place the answer already exists. A receipt that still has quantity remaining is stock that arrived on its posting date and has not left — so **the age of the stock is the age of the layer it is still sitting in**. Nothing extra has to be recorded for this to work.

Under FIFO this is exact. Under average costing it is an approximation, because layers are still consumed oldest-first for quantity even though the cost is averaged across them. That is the right approximation: the report is answering how long the goods have physically been on the shelf, which is what somebody hunting for slow stock is asking, rather than anything about what they are worth.

Selling the oldest stock empties the oldest band, because the layer it came from is the thing that drained.

**Only stock that is actually there is aged.** A location that has gone negative has no open layers for the part it is short by, so it shows only what is physically on the shelf and will not agree with the valuation beside it. That is correct — you cannot age goods that are not there — but it is worth knowing before comparing the two columns.

## Velocity

How much of each item went out over a period, what it cost, what is left, and how long the rest would last.

**Items that never moved appear.** They are the reason anybody runs this. A velocity report that lists only what sold cannot answer the question it exists to answer.

Rows are ordered slowest first for the same reason.

### Blanks are not noughts

Turns and days of cover are left empty where they have no answer:

- **No turns** means there is no stock to divide by — an empty shelf. That is different from **nought turns**, which means there is stock and none of it sold.
- **No days of cover** means nothing sold, so what is left would last for ever. Printing nought would say the opposite of the truth, and a spreadsheet would then average it.

The distinction matters because these figures get exported, summed and averaged by people who did not run the report.

## What you need to be allowed to run them

`Inventory.Report.Read`.
