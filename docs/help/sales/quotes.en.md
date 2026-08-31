# Quotes

A price offered to a customer, before anybody has committed to anything.

## A quote promises price, not stock

It reserves nothing, moves nothing and posts nothing. That is why quoting for goods that are not on the shelf is ordinary rather than an error: a lead time exists precisely so somebody can sell what has not arrived yet.

What a quote does check is that what it names exists. A customer nobody has entered and an item nobody has entered cannot be quoted for at all. Whether the goods can actually be picked is decided at the despatch bay, on the day, by the costing engine — the same as for any other sale.

## Every quote runs out

A quote carries an expiry, and it is not optional. Costs move and suppliers put their prices up; a quote that never ran out would be a price the company could never withdraw. The arrangement nobody remembers to switch off is always found by a customer holding a piece of paper from two years ago.

The default length comes from a company setting — thirty days out of the box — and can be overridden on any individual quote.

## Accepting

Accepting turns the quote into a sales order, and **the prices go across exactly as quoted**.

This matters more than it sounds. The price list may well have moved between the quote and the acceptance. If it has, the customer accepted the number in front of them and not the number the list now holds. Looking it up again would charge somebody something they never agreed to, and the resulting order would look entirely ordinary in every report.

For the same reason, **an expired quote is refused rather than repriced**. Silently repricing is the same wrong in a different coat. Quote again at today's prices and let the customer see them.

Everything else *is* checked afresh, through the ordinary order path: the location, whether the customer has since been blocked, whether the goods are sellable from where the order says. Those are properties of the order rather than of the quote, and a quote from three weeks ago has nothing useful to say about them.

A quote can be accepted once. Two orders behind one agreement would both look legitimate, and the goods would go out twice.

## Declining

Recording a no is worth doing. The quote is kept exactly as it stood, with the reason if the customer gave one — why business was lost is at least as useful as why it was won, and it is the only thing that makes a win rate mean anything.

A declined quote cannot then be accepted. If the customer changes their mind, quote again.

## The expiry sweep

A quote expires whether or not anybody runs the sweep: accepting reads the date and refuses on its own. The sweep exists so the list reads truthfully, and so that a quote nobody answered can be told apart from one still waiting.

It only touches quotes that are still in draft or sent. An accepted quote is left alone however old it is, because it is the record of what somebody agreed to rather than an offer waiting for an answer.

## What you need to be allowed to do it

Viewing needs `Sales.Quote.Read`. Offering a price needs `Sales.Quote.Create`, which implies read. **Accepting needs `Sales.Order.Create`** — accepting writes an order, and somebody who may quote but not order can offer a price without committing the company to it.
