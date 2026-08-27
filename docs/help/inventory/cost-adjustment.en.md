# Cost adjustment

Cost adjustment is the routine that turns an estimate into a fact.

## Why an estimate exists at all

Goods are often sold before the purchase invoice arrives, or before the receipt has been keyed. The sale still has to leave a cost, so it leaves the best figure available and is marked as estimated. Nothing about the sale waits.

## What the routine does

When the real cost is known — the invoice arrives, the receipt is posted, a price variance is settled — the routine finds every movement that took an estimate from that stock and posts the difference. The correction moves inventory against cost of sales, not against a variance account: the goods were sold, and the only thing that was wrong is how much they cost.

## When to run it

Before closing a period, and before reading a margin report you intend to act on. A margin worked out from estimated costs is a margin that will change.

The report of what is still estimated is the one to watch. A figure that has been estimated for three months usually means an invoice nobody chased.
