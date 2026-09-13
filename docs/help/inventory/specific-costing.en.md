# Specific costing, serials and lots

Most goods are interchangeable: one box of paper is as good as the next, and what matters is roughly what the boxes cost. For those, FIFO or average costing is right.

Some goods are not. Two cars of the same model bought a month apart at different prices are two different things, and when one is sold the sale should cost what *that* car cost. Under FIFO the sale costs whatever arrived first, and both the sale's margin and the value of the car still on the books are wrong by the difference. **Specific costing** makes each unit carry its own cost.

## Serials and lots

A specifically costed item says how its units are told apart:

- **Serial** — every unit has its own number: a chassis number, an engine number, a machine's serial. One unit per number, always.
- **Lot** — a batch received together shares one number and one cost: a delivery of tyres, a batch of medicine. Any quantity per lot.

The two go together. Specific costing with nothing to identify the unit is FIFO under another name, and a serial on an item that is not specifically costed is a number nothing costs by and nothing checks. Both combinations are refused.

The costing method and tracking can be set only before anything has posted for the item. After that they are locked, because they decide what every existing entry meant — units received without serials cannot acquire them later.

## Receiving

A purchase receipt names the units arriving:

- **Serial items:** one serial per unit. A line receiving three cars names three chassis numbers, and posts as three units each with its own cost.
- **Lot items:** one lot for the line.

Refused:

- A tracked item received with no number.
- A serial moving more than one unit.
- A serial that is already in stock — two units cannot share one. A car sold and brought back comes in as a **return**, so it carries the cost it left with.
- Several serials that do not match the quantity on the line.

## Selling

A shipment or an issue names the unit leaving, and it costs exactly what that unit cost when it arrived — plus anything a revaluation or a settled invoice has added to it since.

**A specifically costed unit is never sold ahead of its receipt**, whatever the company allows for other stock. Selling goods that are not there yet is normally allowed and valued at an estimate until the receipt settles it. There is no honest estimate of what one particular car cost, so for these items the unit has to be here, at this location.

## Seeing what is on hand

**Serials and lots** lists every tracked unit still in stock: where it is, when it arrived, what it cost and what it is worth in the books. A lot shows what is left of it.

## Not yet supported

- **Till sales, transfers and return documents** cannot yet name a serial or lot. Tracked items are refused there, with a message saying the number is missing, rather than being moved at the wrong unit's cost. Sell tracked items through a sales order, and receive them on a purchase order.
- Moving a tracked unit to another location is therefore not possible yet. Do not do it with a pair of stock adjustments: that writes one off and writes one on through the variance accounts, which is not what happened.
