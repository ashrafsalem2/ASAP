# Returns and credit memos

A customer sends goods back. Two things have to happen and they are right for different reasons: the stock goes back on the shelf, and the customer stops owing money for it.

## What comes back

A return is taken against the order the goods went out on, line by line. What can come back on a line is **what was invoiced, less what has already come back**.

Invoiced rather than shipped, and that distinction is not pedantry. Goods that shipped and were never billed have nothing to credit — there is no debt to reverse. Those go back by correcting the shipment. Raising a credit memo for them would put money on the customer's account that was never owed.

Taking back more than was invoiced is refused, and the refusal cannot be overridden. Almost everything else this system guards is a judgment somebody is entitled to make differently; this one is not a judgment. It puts stock on the shelf that never existed and credit on an account that was never owed, and both of them stay there.

## What the goods are worth

The stock comes back at **what it cost when it left**, not at what the item costs today.

This is the whole reason a return names the order it came back on. Sold at 10, returned in a month when the item costs 30: valued as an ordinary receipt, twenty appears in the inventory account out of nothing while the original sale's cost of sales stays at 10. A customer changing their mind would have moved the company's books.

Where the sale drew on several receipts at different prices, the cost is the average of what actually left on that document — exact whenever the sale came off one layer, which is nearly always, and the closest true answer available when it did not. See [Goods coming back](../inventory/returns) for how the costing engine works this out.

## What the customer is credited

The credit memo is the invoice run backwards, line for line, at the prices the customer was actually charged.

Not at today's price, and not at a price somebody types. A credit memo is the undoing of a specific invoice; if it could disagree with what was billed, then an invoice and a credit memo together would be a way of moving money that no report could ever explain.

Revenue is debited at list and the discount credited back, which is the exact mirror of how the invoice posted. Netting the two would give the same profit and lose the answer to how much was discounted and how much of that came back — and a discount that can only be given and never taken back is a figure that drifts a little every time a customer changes their mind.

The tax is credited at **the rate the sale was made at**, read from the order date rather than from today. A rate change between the invoice and the return would otherwise credit a different amount of tax than was charged, and the difference would sit in the tax account with nothing to explain it.

## What it does to the reports

The return carries a negative sales amount onto the item ledger, so the margin report sees the sale come off. Without that, an order that was entirely returned would keep showing the margin it had on the day it shipped.

The credit posts against the branch the goods went back to, not against whoever keyed it — the same rule as every other document, and the reason branch performance reconciles to the income statement.

## What you need to be allowed to do it

`Sales.Return.Post`, which is marked sensitive and is worth holding separately from whoever raised the invoice. A credit memo takes money off what a customer owes and puts stock back on the shelf; both are worth a second pair of hands.
