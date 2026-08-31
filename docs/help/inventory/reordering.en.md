# Reorder policies

A reorder policy says when a place should order more of an item, and how much. Without one, nothing is suggested and somebody decides by looking at the shelf.

Policies are **per location**. A level that is right for a central warehouse is wrong for a branch that gets a delivery twice a week, and one figure serving both is one of them being wrong. The figures on the item card stay as the company-wide default for anywhere with no policy of its own.

## When it orders

The **reorder point** is the level at or below which the item is reordered. At the point, not below it: the point is a level to be at, not one to pass through.

What is measured against it is not what is on the shelf. It is:

> on hand − promised to somebody else + already on order

A shop with two cases on the shelf and forty on a lorry does not need forty more. This is the whole reason the worksheet exists rather than a report of low stock, and it is the thing that goes wrong when people build this by hand: a run that only looks at the shelf suggests the same order every morning until the goods land, and nobody notices until four times what was wanted turns up on one delivery.

## How much it orders

Two kinds, and the choice is about what actually constrains you.

**A fixed quantity** orders the same amount every time, whatever the shortfall. Right where the quantity is decided by something outside the shop — a pallet, a case, a minimum the vendor will ship. Ordering thirteen because thirteen is the shortfall is not an option when the vendor sells them in twelves.

**Up to a maximum** orders enough to bring stock back to a level you set. Right where the constraint is shelf space or cash rather than the vendor. The order varies with the shortfall, which is what stops a slow week filling the stockroom.

A maximum below the reorder point is **refused, not saved**. It would order at ten and stop at five, so every run would ask for nothing — a policy that sits in the list looking configured and contributes nothing forever. The moment somebody types it is the only moment anybody is looking.

## Minimums and packs

The **minimum order quantity** is the least the vendor will ship. A suggestion below it is lifted up to it.

The **order multiple** is the pack the item comes in. The suggestion is rounded **up** to the next whole pack, never down — rounding down would leave the shop below the level it just decided it needed to be above, and tomorrow's run would suggest the same order again.

Where both are set, the pack has the last word. A minimum of ten from a vendor selling in twelves is an order of twelve: the pack is the only one of the two that is a physical fact.

## What the policy does not do

It does not place an order. It does not reserve anything, cost anything, or commit you to a vendor. It produces a suggestion on the replenishment worksheet, and somebody decides. See the purchasing topic for what happens next.
