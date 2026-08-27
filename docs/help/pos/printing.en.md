# Print templates

A receipt layout is data, not code. The reason is what actually gets changed about one: a shop adds a line about returns, a tax authority wants a phrase in a particular place, a branch wants its own telephone number at the bottom. None of those is worth a release, and a system where they are is a system whose receipt says whatever it said on the day it shipped.

## The language, in full

There are three things in a template.

**A placeholder**, written {Total} or {Total:N2}. It takes a value from the receipt. The format is the same one messages use, so a date on a receipt and a date in a refusal come out identically — and neither can quietly switch calendars because somebody changed the language.

**A repeated region**, written [[lines]] … [[/lines]]. Everything between the tags is printed once per line, and inside it the placeholders come from that line. [[tenders]] does the same for how the sale was paid. A region with nothing in it prints nothing, which is what an empty receipt should look like.

**Everything else**, which is printed exactly as written. Spaces included: a receipt is a fixed-width document and the spacing is the layout.

## The width matters

A receipt roll is a fixed number of characters across — forty-two for the usual eighty-millimetre roll, thirty-two for the fifty-eight. Set it correctly and the preview shows the paper truthfully. Set it wrong and every long line wraps in the middle of a word on the day the printer arrives.

## Preview against a real receipt

The editor renders against an actual posted receipt, not an invented one. A layout that looks right beside made-up figures is how a receipt ships with a total column too narrow for four digits, and the first anybody knows is a queue.

## A placeholder nobody supplied

Stays visible, exactly as written. A receipt with {Totl} printed on it is obviously broken and gets reported within the hour; one with a silent gap where the total should be prints two hundred times before anybody notices.

## Which template a till uses

The branch's own where it has one, and the company's where it does not. That is the whole reason a template can name a branch: a shop wanting its own telephone number at the bottom should not need its own installation.
