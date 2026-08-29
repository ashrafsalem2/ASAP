# Currencies and exchange rates

A company keeps its books in one currency and does business in several. Everything in the general ledger is in the company's own — that is what makes a trial balance add up — so anything written in another has to be converted, and the conversion is only ever as good as the rate it used.

## The company's own currency is not in the list

It is on the company record. It never needs a rate against itself, and giving it a row would invite somebody to enter one — at which point every figure in the system quietly depends on whether that row happens to say 1.

Leave the currency blank on anything in the company's own currency. Marking a line with it does no harm — it is recognised and posted as an ordinary amount — but a blank is clearer and never sends anybody looking for a rate.

## Rates are dated, and only dated

A rate applies from the day it starts until the next one starts. An invoice raised last March is converted at last March's rate, forever, and entering today's rate does not reach back and restate it.

You can enter rates ahead of time. A treasury desk that publishes tomorrow's rate today is doing the right thing, and it will not affect anything posted this morning.

Rates are entered as a pair: how many units of the foreign currency, and what they are worth. Usually that is 1 and the rate. For a currency worth a small fraction of yours it is 100 and the rate — 100 JPY = 2.53 SAR, rather than 1 JPY = 0.0253 SAR, which is the first one rounded and puts the rounding error into every line instead of the last one.

## What happens when there is no rate

The posting stops and names the currency and the day.

This is deliberate. A missing rate is not nought and it is not yesterday's; it is a figure nobody has entered. Guessing one would put a number into the ledger that reconciles perfectly and is wrong by however far the currency has moved — the worst kind of wrong, because nothing will ever flag it. Stopping costs whoever keeps the rates about five seconds. Finding it afterwards costs an afternoon.

## Settling a foreign invoice

An invoice for USD 1,000 raised when the dollar was 3.75 is booked at 3,750. A payment of USD 1,000 arriving when it is 3.80 is booked at 3,800.

The customer owes nothing — they were billed a thousand dollars and they sent a thousand dollars — so both entries close. Settlement of a foreign entry is decided in its own currency, never in yours; measured in riyals the payment would overshoot, leave the invoice fractionally open, and put a chaser in front of somebody who has paid in full.

That leaves 50 riyals on the receivables account belonging to nobody. It is not an unpaid balance. It is what the same thousand dollars was worth on two different days, and it is posted as exactly that: to **exchange gain**, or to **exchange loss** when the rate moved the other way.

Both accounts sit apart from revenue and from costs on purpose. Nothing was sold and nothing was bought; a weak riyal should not be able to make a good month out of a bad one.

Set the two accounts under Finance setup. Until they are set, a settlement that produces a difference is refused rather than posted half-done.

## What cannot be settled against what

An entry in one currency and an entry in another. Doing it would mean choosing a rate to compare the two at, and whichever rate was chosen would decide a gain or a loss that nobody agreed to.

A payment made in riyals against an invoice in dollars is genuinely two things — a payment and a conversion — and is entered as two things.

## What is not here yet

**Revaluation.** What an open foreign balance is worth moves every day, and nothing yet restates it at a period end. Until that exists, the receivables figure on a balance sheet is what the open invoices were worth on the days they were raised, which is right for the ledger and behind the market. The realised difference above — the one that arises when something is actually settled — is handled in full.

**Foreign bank accounts** and the difference between what a balance was worth when money went in and when it came out.
