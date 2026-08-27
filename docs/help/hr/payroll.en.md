# Payroll

A run works out what everybody is owed for a period, and posts it once you commit to it.

## Calculating

A draft run reads everybody who worked any part of the period, including people who left partway through: somebody who left on the tenth is owed ten days, and a run that only read current staff would not pay them.

Each wage is split across the branches the days were actually worked at, using the branch history. The end-of-service charge and any deduction divide the same way and by the same rounding rule, so a shop's figures cannot disagree with themselves.

A part month is a thirtieth of the wage per day, not a division by the days in the calendar month. Dividing by the calendar makes a day of February worth more than a day of March, and nobody's work is worth more for being done in a short month.

## Posting

Posting charges one debit per branch. A single debit for the whole run would balance just as well and leave nobody able to say what a shop costs to staff, which is the question the whole branch history exists to answer.

The credit is net pay owed. Posting a payroll does not pay anybody — conflating the two is how a company comes to believe it has paid staff it has not.

## Two runs over the same days

Posting a run over days a posted run already paid is refused, because it would owe everybody twice for the overlap. It is overridable with a reason, because a correction run is a real thing that does exactly that. Overlapping drafts warn at calculation time, while somebody can still decide they meant the other one.

A draft can be thrown away. A posted run cannot: it is reversed, so the ledger shows both what was done and that it was undone.
