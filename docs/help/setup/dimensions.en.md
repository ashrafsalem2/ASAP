# Dimensions

A dimension is a way of cutting figures that is not an account: a department, a project, a cost centre, a campaign.

## Why not just more accounts

Because the alternatives multiply. Four departments and three projects as accounts is twelve accounts per line of the chart, and the chart becomes unreadable long before the analysis becomes useful. As dimensions it is two values on an entry, and any report can group by either.

## Shortcut dimensions

Two dimensions can be marked as shortcuts. Their values are copied onto every ledger entry, so a report can group by them without joining to the dimension set — which matters on a table of millions of rows. Choose the two you will filter by daily; the rest are still available, just a join away.

## Making one mandatory

A dimension can be required on postings to certain accounts. Worth doing where the analysis is the point of the account: a marketing spend account with no campaign on half its entries answers nothing. Worth avoiding where it merely inconveniences somebody — a mandatory dimension is a refusal, and a refusal at the wrong moment is worked around rather than obeyed.

## Values are a fixed list

Each dimension has the values it may take, and a document naming anything else is refused.

That is deliberate. An axis anybody can type a new value into stops being an axis and becomes a comment field: "Sales", "sales", "Sales dept" and "SLS" are four departments to a report and one to a human, and no amount of care afterwards separates them.

A value can be a **heading** or a **total** rather than a standard one. Those are things to report under, not things to post to — posting beneath a subtotal that also sums the entries below it counts them twice on any report showing both — so they are refused at posting with their own message.

## Naming them on a document

A document names dimensions the way a person does: `DEPARTMENT` is `SALES`. The whole document can name them once, and any line can override.

The override is per dimension, not wholesale. A line naming a project keeps the department the document set, because that is what naming a project means. A line that wants no department clears it explicitly.

## When something is wrong with a code

Each mistake gets its own refusal, and they are all reported at once.

A dimension that does not exist, a dimension that has been retired, a value that does not exist and a value that cannot be posted to are four different problems that send you to different places. One message covering all of them sends you to none. And somebody who mistyped three codes should be told three times, not told about the first, fix it, and be told about the second.

## Retiring one

Blocking a dimension stops anything new being analysed by it. Everything already posted against it is untouched and still reports — which is the point of retiring rather than deleting. The same is true of a single value.
