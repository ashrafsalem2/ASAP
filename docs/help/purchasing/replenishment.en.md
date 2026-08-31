# Replenishment worksheet

The worksheet reads the reorder policies, works out what needs buying, and shows you the arithmetic. It suggests. It does not buy.

## Reading a line

Every figure that produced the suggestion is on the line, because a number nobody can reproduce is a number nobody acts on:

- **On hand** — what is on the shelf at that place.
- **Reserved** — what is already promised to somebody else's order. It is not yours to sell twice.
- **On order** — what is bought and not yet received.
- **Projected** — on hand, less reserved, plus on order. This is the figure compared against the reorder point.
- **Reorder point** — the level from the policy.
- **Suggested** — how much to order, after the vendor's minimum and the pack size have had their say.

If a suggestion looks wrong, the four figures above it say why. Usually the answer is on order: goods that were bought last week are already covering the gap.

## What counts as on order

Everything on a purchase order that has not arrived, except orders that have been cancelled or rejected.

An order still waiting for approval **does** count. It is a request somebody has already made, and suggesting the same goods again would put two of them in front of the same approver.

Getting this wrong is expensive in both directions. Count too little and the worksheet orders it all again. Count too much and it never orders at all, and the shelf goes empty while the figures look healthy.

## Taking the suggestions

Selected lines become a **requisition** — not a purchase order.

That is deliberate, and it is the most important thing on this page. A run that placed its own orders would be a rule nobody wrote spending money nobody approved. The requisition goes through exactly the approval the amount calls for, the same as one typed by hand, and whoever approves it can see the arithmetic in the justification because they did not run the worksheet themselves.

You may take some lines and leave others. Nothing is remembered between runs: leave a line today and it will be suggested again tomorrow, which is the right behaviour — the shortfall has not gone away.

## When nothing is suggested

Three ordinary reasons, in the order worth checking:

1. There is no policy for that item at that place. Policies are per location.
2. The policy is switched off.
3. Projected stock is above the reorder point — most often because goods are already on order. Turn on **show satisfied** to see those lines with their figures rather than guessing.
