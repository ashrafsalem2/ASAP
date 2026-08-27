# Customers and vendors

A customer or a vendor is a party with a ledger of its own. Every invoice, payment and credit note lands in that ledger as well as in the general ledger, and the two must agree.

## The control account

Customer balances are summed into a receivables control account, vendor balances into payables. Neither may be posted to by hand: if a journal could adjust receivables without touching a customer, the ledger and the customer's statement would disagree and nothing would say which was right.

A party may name its own control account where a balance has to be shown separately on the face of the balance sheet. Worth doing rarely — an account per customer makes a chart nobody can read.

## Applying payments

A payment does not settle an invoice by itself. Applying it is a separate act: deciding which payment settled which invoice. That is why it is a separate permission, and why an application can be undone — it changes what a customer is shown as owing without moving a single figure in the general ledger.

An application is refused where it would settle more than is outstanding, where the two entries pull the same way, or where they belong to different parties.

## Credit limits

A customer over their limit is refused at the point of posting, not at the point of ordering, and the message says what they owe and what the limit is. Somebody holding the override permission may post anyway, with a reason, and it goes to the audit log.
