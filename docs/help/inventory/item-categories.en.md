# Item categories

How items are grouped, and which accounts each group posts to.

## Accounts live on the category

That is the reason the grouping exists at all; naming things is a side effect. A company with
twelve thousand items maintains six sets of accounts rather than twelve thousand.

Four accounts per category:

| Account | What lands there |
| --- | --- |
| Inventory | The value of stock held |
| Cost of sales | What the goods sold cost |
| Sales | Revenue earned |
| Variance | Adjustments, and estimates settled later |

An item can be in no category at all. It still trades and is still valued — it simply has no
accounts, which brings us to the thing worth understanding about this screen.

## A category with no inventory account posts nothing

Not an error. A stock movement under it writes its item ledger entry, writes its value entries, and
raises no ledger lines at all. Deliberately: refusing the movement would stop a shop trading over a
setup step nobody has reached yet.

The cost of that choice is real. A company can run for months with its inventory account frozen and
nothing ever says so, because nothing failed.

So the panel at the top of the screen says so. It lists every category missing an account, the items
under it, and — the part that matters — **the value that has already gone unposted because of it**.
A screen saying "four accounts are blank" is a chore. One saying "and 84,000 riyals of stock
movement never reached the ledger" is a decision.

Items in no category get their own row. They have the same problem for a different reason, and
leaving them out would understate the figure by exactly the items nobody has classified.

## Account numbers are checked

Against the chart, as the category is saved. Three answers are refused:

- **No such account.** Saving it would leave a category that looks configured and posts nothing.
- **A heading or a total.** Those carry no balance of their own; nothing ever lands on them.
- **A blocked account.** Withdrawn on purpose, and the fix is to unblock it or choose another.

On an installation with no general ledger nothing is checked, because there is nothing to check
against. Running stock without accounts is a supported way to run, and turning an unanswerable
question into a refusal would make it not one.

## The hierarchy

A category can sit under another. Two things are refused: a category made its own parent, and a
parent that already sits underneath the category being saved. A circle would make anything that
walks the tree run for ever, and walking it is the point of having a parent.

Accounts are not inherited from a parent today. A child category with blank accounts posts nothing,
the same as any other, and the gap panel says so.

## Moving an item between categories

Movements already posted keep the accounts they posted to.

A category is where an item's accounts are read from **at the moment of posting**, not a claim about
where its history went. Restating a closed month by regrouping a catalogue would be a surprising
thing for a dropdown to do.

## What is not here yet

**Inherited accounts.** A child could fall back to its parent's accounts; it does not.

**Posting the gap once it is fixed.** Setting an account does not go back and post the movements
that were made while it was blank. They stay in the value entries, correct, and the ledger stays
short by that amount until somebody posts a journal for it.

**Categories on the item screen.** An item's category is set from here rather than from the item.
