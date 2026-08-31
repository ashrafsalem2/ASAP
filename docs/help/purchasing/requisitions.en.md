# Requisitions

Somebody asks for something to be bought. That is all a requisition is, and everything about it follows from that.

## A need, not a purchase

A purchase order names a vendor, a price and a commitment. A requisition names **a need**. Who to buy from may not be known, and what it will cost is a guess by whoever is asking.

So nothing here posts and nothing here commits the company to anything. It belongs with whoever runs out of things rather than with whoever places orders — which is why raising one needs only `Purchasing.Requisition.Create`, and turning one into an order needs `Purchasing.Order.Create` as well.

## The estimate

Every line carries an estimated unit cost, and it is named as a guess because that is what it is.

It matters for exactly one thing: deciding whether the requisition needs signing for. Below the company's approval threshold it goes straight through, signed by nobody. That is not a gap — the threshold is a number somebody chose, and choosing it is the decision.

## Nobody signs for their own request

An approval you can give yourself is a checkbox, not a control. The whole point of the exercise is that a second person looked, so the person who raised a requisition cannot approve it however senior they are.

The justification field is what that second person actually reads. "Twelve reams of paper" is a description; "the Jeddah shop has run out and it is the till receipt printer" is a reason to sign.

When somebody signs, the estimated total is **frozen** on the record. An approval is authority for an amount rather than for a document number, so anything that moves the total afterwards has to ask again.

## Approving a requisition is not approving the orders

This is the rule worth understanding.

An approved requisition is authority **to buy the thing**. It is not authority to buy it at any price. The approval was measured against an estimate somebody typed, and the estimate is the one number on the document nobody has checked.

The orders that come out of it carry real prices from a real vendor, and they go through the purchase order's own approval on those figures. A requisition approved at four thousand does not let an order for forty thousand through.

## One requisition, several orders

A shop asking for paper, bolts and a kettle is asking one question and will get three answers. So a requisition becomes as many orders as it has vendors, raised one at a time.

Each line counts how much of it has already been turned into an order. That counter is the only thing standing between a line and being bought twice, and it is checked rather than trusted: ordering past what was asked for is **refused, and cannot be overridden**. A requisition is authority for a quantity, and buying past it commits the company to something nobody signed for.

The prices go on the order when the order is raised, not from the requisition. The suggested vendor on a line is exactly that — a suggestion. Whoever raises the order decides, and may well decide differently.

## Cancelling

A requisition that has already produced orders cannot be cancelled. Those orders are real and somebody is expecting the goods; cancelling the requisition would leave them pointing at a document saying nothing was ever wanted. Cancel the orders instead.

## What you need to be allowed to do it

| Doing | Needs |
| --- | --- |
| Looking | `Purchasing.Requisition.Read` |
| Asking, submitting, cancelling | `Purchasing.Requisition.Create` |
| Signing or turning down | `Purchasing.Requisition.Approve` |
| Raising the order | `Purchasing.Order.Create` |
