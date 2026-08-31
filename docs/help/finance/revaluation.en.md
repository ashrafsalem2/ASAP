# Currency revaluation

An invoice for a thousand dollars is carried in the books at what a thousand dollars was worth on the day it was raised. The customer still owes a thousand dollars — that never changes — but what the company will get for them does, and a balance sheet still showing the old figure is claiming an amount nobody will receive.

Revaluation restates the open foreign balances at the rate on the day being closed, and posts the difference as an exchange gain or loss.

## What it changes and what it does not

It changes the **base-currency** carrying amount of each open balance, and posts the movement to the control account and the exchange gain or loss account.

It never touches the amount in the foreign currency. The customer owes the same thousand dollars after the run as before. A revaluation that moved that figure would put a chaser in front of somebody for money they do not owe.

## Running it twice

Nothing happens the second time.

The run measures against what each balance is **carried at**, not against what it was worth when raised. After the first run the carrying amount already is the closing valuation, so the difference is zero and nothing posts.

That is why there is no reversal entry at the start of the next period, and why none is written. A reversal somebody forgets to make, or makes twice, is a whole class of error this design does not have. Close June, then close July, and July's entry is the movement from June's rate to July's — which is exactly what it should be.

## Preview first

The preview shows every balance that would move, with all five figures: what is owed in the currency, what it is carried at, the closing rate, what it is worth at that rate, and the difference. Nothing is hidden behind a total.

Balances already carried at the closing rate are left out entirely rather than listed at zero, so the handful that moved are not buried under every one that did not.

## When it refuses

**No rate for the closing date.** Refused once for the currency, not once per invoice — forty invoices in a currency with no rate is one thing wrong, not forty. Enter the rate and run again.

**No gain or loss account set up.** The difference has nowhere to go. Set the two accounts under the currency settings.

## Dating

The entry is dated at **the day being closed**, not at today.

This differs deliberately from the exchange difference posted when an invoice is settled, which is dated at today: that difference did not exist until the two entries met. A revaluation is a statement about what a balance was worth *on a date*, and dating it anywhere else leaves the balance sheet at that date still saying the old figure.

If the period is closed, the posting is refused like any other. Reopen it, run the revaluation, and close it again.

## Customers and vendors

Separate runs, because they post to different control accounts and because most companies close one before the other.

The sign works out on its own. A receivable worth more in riyals is a gain; a payable worth more is a loss.
