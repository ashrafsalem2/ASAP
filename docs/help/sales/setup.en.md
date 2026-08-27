# Sales setup

The accounts here are the ones a sale writes to, and each is owned by the module rather than open to a journal.

**Revenue** is where sales land, at list price. **Sales discount** is where the difference between list and what was charged lands, as a contra. Keeping them apart is what lets a report say what was given away rather than only what was taken.

**Receivables** is the control account customer balances sum into. It is set in the finance settings, not here, because vendors use the same mechanism.

## Numbering

Sales invoices and credit memos come from gapless series and are issued in date order, because both are tax documents. Sales orders do not need to be either: an order number with a hole in it is nothing at all.

## Defaults worth setting

**The default location** decides where stock leaves from when nobody says. Set it to the place that ships most, and expect to override it.

**Whether an order may ship beyond its quantity.** Off is the sane default; the difference between ordered and shipped is a conversation with the customer rather than a warehouse decision.
