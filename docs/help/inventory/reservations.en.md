# Reserved stock

Stock that has been promised to one document and is therefore not available to any other.

## A reservation is not a movement

Reserving posts nothing. No ledger entry, no cost, no transaction number, nothing for the settlement routine to come back to. What is on hand does not change by a single unit.

What changes is what is **available**, and the difference between those two figures is the whole idea:

> **on hand** is a fact about the shelf
> **available** is that fact, less what has already been promised to somebody else

Without reservations an order promises goods and nothing stops the next order promising the same ones. Both look correct until the second van is being loaded, and the person who finds out is a customer.

## Reserving more than is free is refused

This is a deliberate difference from selling into negative stock, and the reason is who is standing there.

A sale below zero is a real decision somebody makes with a customer in front of them and the goods visible on the shelf; the system permits it, values the shortfall as an estimate and says so. A reservation is planning. It is made at a desk, with no urgency and nobody waiting. A promise made against stock that does not exist is not a promise, and there is nothing to be gained by letting it be made quietly.

So reserving refuses, and the refusal says what is on hand, what is already promised, and what is left.

A document is never blocked by its **own** reservation. Adding to what an order already holds is fine, and shipping what it reserved is exactly what the reservation was for.

## Shipping stock somebody else was promised

If a movement would take stock that another document is holding, it is **blocked** — and it can be pushed past by somebody holding `Inventory.Stock.Override`, which records who did it and why.

Blocked rather than refused outright, because the goods are on the shelf and a shop must be able to sell what it can see. What must not happen is that it is silent: the order that was promised those goods should not find out at the loading bay.

Receiving goods is never blocked. Nothing coming in can take anybody's reservation.

## Shipping consumes the reservation

When the goods go out against the document that reserved them, the reservation falls by what left. A part shipment keeps holding the rest, which is what a part shipment should do.

Shipping more than was reserved is not an error. An order may reserve five and ship ten: the five it never reserved were simply never held, the reservation falls to nought, and the rest comes off free stock like anything else.

This happens inside the posting engine rather than in whoever ships, so it works whichever door the goods leave by — a sales shipment, a transfer, a till.

## Releasing, and stock that gets stranded

Releasing lets the stock go and **keeps the row**, with the reason if one was given. A reservation that vanished when it was released could not answer what was held, for how long, or who let it go — which is exactly what somebody asks when an order could not be filled.

That leaves one thing worth watching. An order that is abandoned rather than cancelled goes on holding its stock for ever, and nothing will tell you. **Read the outstanding list.** It is the only place that stranded holds show up, and a reservation against an order nobody is working on is stock the company cannot sell and does not know it has.

## On a sales order

An order has a *hold stock* action. It is something somebody does, not something releasing an order does by itself.

Reserving automatically would put every released order in the company into competition for the same stock whether or not anybody wanted that, and a warehouse that picks straight off the shelf would find its orders refusing each other for reasons nobody asked for. A per-item "always reserve" policy is a reasonable thing to want and is not built yet.

Lines are held one at a time, and a line that cannot be held in full is not held at all — the rule above never bends. What comes back names every line that could not be held and why, so holding four lines out of five is a useful outcome rather than a silent one.

## What you need to be allowed to do it

Seeing what is held needs `Inventory.Reservation.Read`. Holding and releasing needs `Inventory.Reservation.Update`.

It is worth a permission of its own precisely because it moves nothing. Nothing posts, nothing looks wrong, and no report shows a problem — until the goods are wanted.
