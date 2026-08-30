# Item variants

The colours, sizes and flavours an item is stocked as.

## A variant is not a bin

A bin says where the same goods are standing, and never touches a quantity or a cost. A variant is
a different physical thing. A blue shirt in medium and a red one in large are not interchangeable,
cannot be picked for one another, and may not even have cost the same.

Which is the whole difficulty. Stock, cost layers and valuation are all per item and location; a
variant splits every one of them again. **A query that forgets the variant does not fail** — it
costs a blue shirt against a red receipt, and the only symptom is a margin quietly wrong on both.

## Off by default, and off means unchanged

An item without variants behaves exactly as it always did. Every entry carries no variant, and none
of the arithmetic changes. That is deliberate: it means turning variants on somewhere cannot break
anything anywhere else.

Adding the first variant to an item turns them on. Making somebody set a flag as well would be a
second step whose only job is to agree with the first, and its failure mode is a variant nothing can
be posted against.

## With variants on, every movement says which one

Refused otherwise, with no default and no softening — unlike a bin, which falls back to the
receiving bay for goods coming in.

There is nothing to fall back to here. "No variant" on an item that has them is not a vaguer answer,
it is a different stock line: a phantom one that no shelf corresponds to, and the first sale out of
it would cost against nothing.

A variant named on an item that has none is refused too. Recording one that nothing reads would look
like tracking that never happened.

## Each variant keeps its own cost

Received blue at 40 and red at 60, and selling five of each costs 200 and 300. Neither reaches into
the other's receipts.

A variant also remembers what it last cost, separately from the item. Once variants exist the item's
own figure becomes whichever variant was received most recently, which makes it a poor thing to
value a shortfall at — estimating a blue shirt at fifty because a red one cost fifty is a worse
guess than blue's own history. A variant never received falls back to the item's figure, because a
first sale of something never bought has nothing better to go on.

## Selling before receiving

Works exactly as it does without variants, per variant. A sale of blue with none on hand goes
negative on blue, is valued at an estimate, and settles when **a blue receipt** arrives. A red
arrival never pays for a blue sale.

## Barcodes

A variant carries its own, and it is looked for before the item's — the same order as a unit's. A
shop scans the label on the garment, which carries the size. An item barcode shared across sizes
would make every scan ambiguous at exactly the moment nobody has time to resolve it.

A barcode already on an item, one of its units, or another variant is refused. Two rows sharing one
barcode makes a scan return whichever the database reached first.

## Blocking and turning off

**Blocked** stops goods arriving, not leaving — what is already in stock under a withdrawn variant
is still on the shelf and still has to be sellable.

**Turning variants off** is refused while stock still stands under them. Every one of those entries
would keep pointing at a variant nothing reads, and the item's cost layers would silently merge
colours that were never interchangeable. Clear the stock first.

## What is not here yet

**Variant dimensions.** `BLUE-M` is one code, not a colour and a size that can be filtered
separately. A shop wanting "everything in medium" across a range cannot ask for it.

**Generating variants from a grid.** Six colours by five sizes is thirty rows, entered one at a
time.

**A price per variant.** All variants of an item share its price. That is right for sizes and wrong
for anything where one version costs the customer more.
