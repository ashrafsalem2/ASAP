# Requests for quotation

The same question, asked of several vendors at once, so the answers can be put side by side.

## Nothing here commits anybody

A requisition says what is needed. A request for quotation asks what it would cost. The vendors are quoting and the company is comparing; the only thing that becomes real is the order raised from an award.

A request that was sent to nobody cannot go out — it would sit waiting for answers from an empty list. And a quote from a vendor who was never invited is refused, because letting one in would allow a supplier to add themselves to a tender.

## Silence is information

A vendor who was asked and said nothing is tracked separately from one who declined. That distinction is worth having: it is the difference between a supplier who cannot help this time and one who does not answer their post, and it is exactly what you want to know before asking them again.

## The comparison

Cheapest and fastest are flagged **separately**, because they are usually different vendors.

A comparison that showed only money would make the choice look obvious when it is not. The cheapest supplier who takes six weeks is the wrong answer for a shelf that is empty now, and the price alone cannot say so.

## Awarding is per line

Real buying is per line: the bolts go to one supplier and the nuts to another. Forcing one request onto one vendor would either lose the better price or split the question into several that nobody can compare.

A line can only be awarded to somebody who actually quoted for it. Awarding to a vendor who never priced it would invent an agreement.

## The rule this exists for

**Awarding to anything other than the cheapest quote is refused unless a reason is given.**

Choosing the dearer supplier is a legitimate decision and often the right one — a fortnight's lead time is worth paying for when the shelf is empty, and a supplier who has never yet been late is worth a premium. None of that is in question.

What is in question is silence. This is the decision somebody asks about a year later, and a blank field is the difference between an answer and an investigation. The refusal names the gap: who quoted less, by how much, per unit.

The reason is kept on the line for as long as the record exists.

## Turning an award into an order

Once lines are awarded, an order is raised per winning vendor, and it carries **the price they quoted**.

This is the one place a request for quotation differs from a requisition. A requisition's estimate was a guess, so the order needed a real price typed in. A quote *is* the real price, and retyping it would only create a way for the order to disagree with what was agreed.

An award that has already become an order cannot be moved to another vendor. The order is real and somebody is expecting the goods; moving the award now would leave that order pointing at a decision that no longer says what it says. Change the order instead.

## What you need to be allowed to do it

Viewing needs `Purchasing.Quotation.Read`. Asking, recording answers and awarding all need `Purchasing.Quotation.Update` — asking commits nothing and recording commits nothing, and awarding is a decision but not a posting. Raising the order needs `Purchasing.Order.Create`, because that is where the company commits.
