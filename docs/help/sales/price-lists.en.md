# Price lists

A price list says what a particular customer pays, rather than what the item says. Without one, everybody pays the price on the item — which is the right answer for somebody walking into a shop, and useless for anybody selling to trade, where the whole commercial arrangement is that this customer pays less than that one and both pay less than the counter.

## Where a price comes from

A sales order line takes the first of these that has something to say:

1. A price typed on the line. Whoever took the order may have agreed something on the telephone that no list knows about, and they are entitled to.
2. The price on the list the customer is assigned to.
3. The price on the item.

Nothing is skipped and nothing is silent. If the second step cannot decide, the order is refused rather than falling through to the third — see below.

## Which line wins

A list can hold several prices for the same item, and more than one can fit the sale in front of you. The most specific one wins:

- a price for one variant beats a price for the item in any variant;
- a price for one unit of measure beats a price for any unit;
- a price from a quantity up beats a price for any quantity, as long as that many are actually being bought.

That is what lets a general trade price and a volume break sit in the same list without either having to know the other exists. Put 80 against the item with no minimum, and 70 against the item from a hundred up, and an order for ninety-nine takes 80 while an order for a hundred takes 70.

## Two lines that say different things

Two lines that are equally specific and disagree are **refused, not resolved**.

This is deliberate and it is the most important rule here. It is not a tie to break: it is a contradiction somebody entered by accident, and picking one of them would make what a customer is charged depend on which row the database happened to reach first. That is a difference nobody finds until an invoice is queried, and by then the same wrong price has gone out several more times.

The order will not be taken until the sheet is fixed. Make one of the two lines more specific, or delete it.

## Dates

A list has a date window, and so does each line on it. Either can expire.

A campaign price agreed for one quarter should carry an end date on the day it is agreed, not on the day somebody remembers. The arrangement nobody remembers to switch off is the one still being honoured two years later, and it is always found by a customer rather than by a report.

An expired line stops applying and the next most specific line takes over. An expired list takes every line on it with it, and the customer falls back to the item's price.

## Assigning a customer

A customer is on one list or on none. Taking them off puts them back on the counter price immediately.

The assignment is held by Sales, not on the customer record. A customer belongs to Finance, and what they pay for goods is a sales arrangement — Finance has no business knowing about price lists, and Sales has no business adding columns to the party ledger.

## Editing a sheet

A price list is saved whole. The lines you save replace the lines that were held, because that is how somebody negotiating a contract thinks about it and because a half-applied sheet is a set of prices nobody agreed to.

## Selling below cost

The below-cost warning is measured against **the price this customer actually pays**, not the price on the item.

This matters more than it sounds. An item that lists at 100 and costs 40 looks perfectly healthy; if the customer's list says 30, the sale loses money and only the agreed price shows it. Reading the item price here would let every contract sale below cost through in silence, which is exactly the case the warning exists for.

It stays a warning rather than a refusal. Clearing old stock at a loss is a real decision somebody is entitled to make. What it must not be is invisible until the margin report three weeks later, by which time the same price has been quoted to four more customers.

## What you need to be allowed to do it

Viewing lists needs `Sales.PriceList.Read`. Changing them needs `Sales.PriceList.Update`, which is marked sensitive: this is the commercial arrangement itself, so it belongs with whoever agrees prices rather than with whoever types orders.
