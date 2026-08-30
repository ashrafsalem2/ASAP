# Goods coming back

What stock is worth when a customer returns it, or when it goes back to a vendor.

## A return is not a purchase

Nothing was bought and nothing new was paid. The goods are the same goods, and they are worth what
they were worth when they left.

That sounds obvious and the obvious implementation gets it wrong. A return is inbound stock, and
inbound stock is ordinarily valued at what the item costs now — so goods sold at 10 and returned
when the item costs 30 come back at 30. Twenty appears in the inventory account out of nothing,
while the original sale's cost of sales stays at 10. A customer changing their mind has moved the
company's books.

## So say which sale it came back on

Name the document and the goods are restored at what they actually cost. A till doing a return
against a receipt already knows it and passes it through; a sales return names the invoice.

Where a sale drew on two receipts at different prices, the return comes back at what that sale
averaged. Fifteen sold at a cost of 200 return at 13.33333 each, because nothing records which of
the fifteen the customer happened to have, and the average of what actually left is the closest true
answer available. It is exact whenever the sale came off one layer, which is nearly always.

## When nobody knows

Sometimes there is no receipt and no document to name. The approximation is unavoidable then, and
the return is valued at what the item costs today.

Passing over that in silence is not unavoidable. The posting says so: what it assumed, what the
figure was, and what naming the document would have done instead. A warning rather than a refusal,
because a shop cannot turn a customer away over its own paperwork — but not nothing either, because
the difference is real money in an account nobody will look at again.

## An explicit cost still wins

Whoever posts the movement may know better than either figure — goods taken back under an agreed
settlement, say. A cost stated on the movement is used as given and nothing is assumed.

## What is not here yet

**Returning a specific unit.** Serial-numbered goods could be restored at exactly what that one
cost; today a serialised return averages like any other.

**A return that reopens the cost layer it came from.** The goods come back as a new receipt rather
than as the old one un-consumed, so a later sale takes them in date order like anything else. That
is right for anything fungible and arguable for anything else.

**Vendor returns against a specific receipt.** The mechanism is the same and takes the same document
reference; nothing in Purchasing passes one yet.
