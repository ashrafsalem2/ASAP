# Sending goods back to a vendor

A delivery arrives faulty, short-dated, or simply not what was ordered. This is how it goes back and how the money comes off.

## Bounded by what arrived, not what was invoiced

This is the one place a purchase return differs from a sales return, and the difference matters.

A customer can only be credited for goods they were **billed** for, so a sales return is bounded by what was invoiced. Goods can be sent **back** to a vendor before their invoice ever turns up — rejecting a faulty delivery at the door is the ordinary case, not the exception. So a purchase return is bounded by what was **received**, less what has already gone back.

Sending back more than arrived is refused, and the refusal cannot be overridden. It would take stock off a shelf that never held it and money off a debt that was never owed; that is not a judgment somebody is entitled to make differently.

## What it posts, in two parts

Because goods can go back before or after their invoice, the posting comes in two halves and only one of them involves the vendor.

**Always, for everything going back:**

```
  Accrual (goods received not invoiced)   debit    what the goods cost
  Inventory                               credit   the same, by the costing engine
```

**And separately, only for the part that had been invoiced:**

```
  Vendor (payables)                       debit    what was billed, plus its tax
  Accrual                                 credit   the net
  VAT recoverable                         credit   the tax, given back
```

Goods returned before their invoice arrives simply unwind the accrual and stop there. There is no debt to reverse, because nobody has asked to be paid yet — and no credit memo is raised at all.

Goods returned after it has arrived unwind both. The accrual nets to nothing across the pair, and that is what says the two halves agree.

## Which goods get credited first

Returns come off the **invoiced** quantity first.

Five arrived, three were invoiced, two go back: those two are treated as invoiced ones and credited. A later return of two more credits only the remaining one.

The alternative — crediting the uninvoiced goods first — leaves the accrual correct only once everything has gone back, and wrong at every step in between. Since most returns are partial and most accruals are read at a month end that falls in the middle, that difference is the whole point.

## What the goods are worth

The stock leaves at **what it cost when it arrived**, not at what the item costs today. That is why the return names the order.

It is the same rule as a customer return and it is wrong in the same way when it is missed. Bought at ten, the supplier's price then rises to thirty, and two units go back: valued at today's cost, twenty of inventory value disappears that the company never had. A change of supplier price would move the inventory account.

Where the return names no order, it falls back to today's cost and the posting says so.

## What you need to be allowed to do it

`Purchasing.Return.Post`, which is marked sensitive. Sending goods back takes stock off the shelf and money off what the company owes — both worth a second pair of hands from whoever received the delivery.
