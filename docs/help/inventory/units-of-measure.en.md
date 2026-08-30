# Units of measure

Something bought by the case, stocked by the tin and sold by the tin. Something else bought and sold by the kilo. A unit of measure is how a quantity is entered and shown — never how it is stored.

## Everything is stored in the base unit

The item ledger, the costing engine, every stock figure and every report work in one unit per item: the base unit, the one the item is counted in.

That is not a preference. **A stock figure with mixed units in it cannot be added up.** An item ledger holding "3" where three might mean three tins or three cases is a ledger that answers nothing, and no amount of care afterwards separates them.

So conversion happens once, at the edge — where somebody is standing at a till or keying a purchase order. One multiplication there buys the ability to answer "how much is there" everywhere else.

## A box is a fact about the item

The unit list is company-wide: `PCS`, `KG`, `BOX`, `CASE`, `PALLET`. What a box *holds* is set per item, because one item's box is twelve and another's is six. Boxes differ; the word does not.

Set the unit on the item and say how many base units it holds. A factor of nought is refused rather than saved — it would turn every quantity in that unit into nought, which reads as a clean zero on every report rather than as a mistake.

## An item sold only in its base unit needs no setup

If everything about an item is counted in pieces, there is nothing to configure. The base unit always works, whether or not anybody set up a row for it, and it is always first in the list.

## Barcodes belong to units, not just items

A case of twelve has a different barcode from a single. Give the case unit its own barcode and **scanning it adds twelve**, not one.

The unit's barcode is looked for before the item's. Getting that order the other way round would make every case scan add a single, and nobody would notice until a stock count.

## Decimal places

Each unit says how many decimal places a quantity in it may carry. None for things counted, three for things weighed.

It is not decoration. A till that accepts 2.5 of something sold one at a time has taken an order nobody can pick, and a scale reporting 1.234 kg against a whole-number unit loses 234 grams on every sale.

## Converting back

Seven tins expressed in boxes of twelve is 0.5833 of a box. The system does not round that away — what it shows you is never what it stores, so stock is not lost to a display decision.

## What is not here yet

**Changing an item's base unit once it has ledger entries.** It would silently restate every historical quantity: a hundred tins becoming a hundred cases. Nothing stops it today beyond it being a bad idea, and a refusal is the right answer.

**Different units for buying and selling by default** — an item bought by the case and sold by the tin has to have the unit chosen on each document rather than defaulted per direction.
