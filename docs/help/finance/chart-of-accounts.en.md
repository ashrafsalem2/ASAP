# The chart of accounts

The chart is the list of accounts every figure in the company ends up in. Two things about it decide more than anything else you will set up.

**An account's category is not a label.** Whether an account is an asset, a liability, equity, income, cost of sales or an expense is what puts it on the income statement or the balance sheet, and what the year-end transfer sweeps out. Categorising a bank account as an expense does not make a report look odd; it makes the profit wrong by the balance of the account.

**A posting account is where figures land; a total account is where they are summed.** Only posting accounts may be written to. Total accounts add up a range and appear as subtotals. A figure posted to a total account would be counted twice — once on its own line and once in the range it falls inside.

## Accounts a module owns

Some accounts refuse to be posted to by hand: receivables, payables, inventory, the tax accounts. They are written by the module that owns them, and the restriction is what keeps the control account and its detail in step. If receivables can be adjusted by a journal without touching a customer, the ledger and the customer's statement disagree and nothing says which is right.

Somebody holding the override permission may still post to one, and every use is recorded in the audit log with the reason they gave.

## Changing an account

A name can change at any time; posted entries carry the name they were posted under, so history reads as it did. A number can change until something has been posted to it. After that the number is on documents somebody has already been sent.
