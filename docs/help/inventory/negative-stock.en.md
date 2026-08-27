# Negative stock

Negative stock is stock sold that the system does not know it had. It happens for ordinary reasons: goods arrive and the receipt is keyed the next morning, a count was wrong, a transfer was shipped and not received.

## Whether to allow it

A company that refuses it stops a till mid-sale over a paperwork delay. A company that allows it sells what it has and sorts the record out afterwards. Both are defensible and the setting is company-wide.

Where it is refused, somebody holding the override permission may still sell, with a reason, and it goes to the audit log.

## What it does not do

Allowing negative stock never corrupts cost. When there is nothing on hand to take a cost from, the movement leaves at an estimate — the last known cost — and is flagged as estimated. The estimate never reaches the general ledger: a figure nobody has confirmed would put the inventory account out of step with the stock valuation by exactly the amount still in doubt.

When the goods that were actually sold are eventually received, the settlement routine works out what they really cost and posts the difference to cost of sales. The cost of that sale ends up right, however far the record ran ahead of the goods.

That is the whole bargain: the business decides whether to trade on faith, and the costing engine settles the truth afterwards regardless.
