# Bins

A location is where goods are. A bin is where in it — an aisle, a shelf, a pallet position.

## A bin is a refinement of a location, never a substitute

Every stock figure, every valuation and every cost layer is per location and stays that way. The
bin only says where inside that location the goods are standing.

That line is worth holding. Costing per bin is tempting and wrong: it would mean the same item at
the same location having two costs depending on which shelf it was picked from, and a stock figure
that has to be summed across shelves before it can be compared to the ledger. **Bins answer "where
is it", not "how much is there" and never "what is it worth".**

The practical consequence is a good one. Turning bins on at a location cannot change a valuation,
so it is not a decision anybody needs to take to finance first.

## A location without bins is a complete location

Off by default. A shop with one stockroom does not need to name a shelf, and being made to would
be a cost with nothing behind it.

Turn them on for the place where "which shelf" is a real question — a warehouse with aisles, where
somebody is sent walking and the walk is the expensive part.

## With bins on, every movement says which one

Otherwise the shelves hold a picture of the stock that is quietly wrong from the first receipt
that skipped one, and nobody finds out until a picker is sent to an empty shelf.

One softening, and only for goods coming in: name a **receiving bin** and arrivals with nowhere
specified land there. Something that has arrived has physically arrived somewhere, and the
receiving bay is that somewhere. Goods going out get no such default — guessing which shelf they
came off is how a bin ends up holding stock nobody can find.

At most one receiving bin per location, because "where do things go when nobody says" has to have
one answer.

## The shelf is short, the location is not

This is the message bins exist to produce. Five were picked from `A-01`, which has two. The
location still holds forty.

Nothing is short except the paperwork about which shelf. So it is a **warning naming the shelves
that do have it**, not a refusal: blocking would stop a picker who is holding the goods in their
hand. Move the stock between bins afterwards to say where it really is.

The bins are listed in pick order, because the answer is going to send somebody walking and the
shortest walk is a fact about the floor plan rather than about how the shelves happen to be named.

## Nothing is on any shelf

A different message, because it has a different answer. The location holds forty and none of it is
in a bin at all.

That is what stock received before the location started tracking bins looks like. Count it onto
its shelves once and the bins agree with the location from then on. The valuation was never
affected.

## Blocking and removing

**Blocked** stops goods arriving, not leaving. What is already in a blocked bin is still physically
there, and refusing to take it out would strand it until somebody unblocked a shelf that is out of
use precisely because nothing should be added to it.

**Removing** is refused while anything is standing in the bin. The stock would not be lost — the
location total never depended on bins — but the only record of where those goods are would be, and
that is the one thing the bin was for. To stop a bin being used without emptying it, block it.

## Codes are unique inside a location

Two warehouses both having an `A-01` is ordinary. Forcing them apart would put the warehouse name
in every code twice.

## What is not here yet

**Put-away and pick suggestions.** The system knows the pick order and what is on each shelf, so
it could propose the walk. It does not yet; somebody still names the bin.

**Bin capacity.** Nothing stops a shelf being told it holds more than it physically can.

**Bin-to-bin movement as its own document.** Moving stock between shelves is possible as two
adjustments today, which posts a value entry of nought and works, but a movement that never leaves
the location deserves to say so on its own.
