# Recurring journals

Depreciation, rent, insurance spread over a year, an accrual for a cost whose invoice has not arrived. Each is the same handful of lines every month, and keying them by hand is a job that gets done twelve times and forgotten once — which is the month somebody finds out about.

A recurring batch is a template. Posting it produces an ordinary journal through the same posting engine as everything else, and then the batch moves its own dates on. Nothing about the entries is special, which is what stops recurring journals becoming a second ledger with its own rules.

## When each line is next due

Written as a step through the calendar rather than a number of days:

- `1M` — a month on
- `1M+CM` — a month on, then to the end of that month
- `3M` or `1Q` — a quarter
- `1W`, `1D`, `1Y` — a week, a day, a year
- `CM`, `CQ`, `CY` — to the end of the current month, quarter or year

**`1M+CM` is what almost every accrual actually wants.** It gives the 31st, then the 28th, then the 31st, then the 30th, each in its turn, without anybody maintaining a list — and it gets 29 February right in a leap year.

A plain number of days is wrong in a way that takes months to notice: thirty days on from 31 January is 2 March, and an accrual landing on the second of the month is one somebody corrects twelve times a year.

The step is taken from the date the line *was due*, not from today. A batch posted three days late is still a monthly batch; stepping from today would walk its date forward through the year every time somebody was on holiday.

## What happens to the amount

**Fixed** — it stays. Rent at a set monthly figure: post it, and next month post it again.

**Variable** — it is cleared after posting, so somebody enters this period's figure each time. For a cost that recurs reliably and varies every time, like a utility bill. Clearing it is the point: a variable line left at last month's figure posts last month's figure, and nothing about the result looks wrong.

**Balance** — posts whatever the account holds and clears it to nothing. For a holding or clearing account that should be empty at each month end. The line does not name an amount because the ledger already knows it.

**Reversing fixed** and **reversing variable** — post the amount, and post the opposite of it the following day.

## Accruals, which are the reason this exists

A cost belongs to March and its invoice arrives in April. Accrue it on the 31st and reverse it on the 1st: March is right, and April is not double-counted when the invoice lands.

Doing that by hand is two journals a month per accrual, and the second one is the one that gets forgotten. A reversing line is one line.

If the accrual posts and its reversal is refused — usually because the following day falls in a closed period — you are told plainly, because that state is worse than neither having posted. The cost would be counted in both periods and nothing later would ask about it.

## What stops it

**Nothing due.** Posting a batch before it is due would put entries in the wrong period, so it waits and tells you when the next one is.

**A line with no amount.** A variable line nobody filled in, or a balance line on an account that is already empty. The two look identical from outside, so the message says which it was; the rest of the batch still posts.

**The posting itself refused** — a closed period, an account that will not take entries, a dimension value that has been retired. Then nothing posts and **nothing moves on**, so the batch is still due. That is what somebody fixing the problem expects. A batch that moved on without posting would be a month of missing entries that nothing will ever ask about.

**A recurrence that cannot be read.** The line posts, and is left where it was rather than advanced by a guess. It stays due, which somebody notices; a line advanced by a guess is wrong quietly.

## Dimensions

Written on the line as `DEPARTMENT=SALES`, and resolved fresh at each posting rather than stored as a fixed set. A recurring line outlives the values it names — a department is retired, a project ends — and a stored set would go on posting to it in silence. Resolved each time, a value that has gone refuses the posting and says so.
