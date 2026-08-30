# The bridge agent

A small program that runs on a till so the browser can drive devices it otherwise cannot reach: a cash drawer, a customer display, a scale.

**Most tills do not need it.** A receipt printer goes through the browser's own print dialog and a scanner types what it read, so a shop with those two needs nothing installed at all. The [devices](devices.md) page tells each till whether it needs the agent and names the devices that make it so.

## What it does and does not do

It opens serial ports and sends bytes. It holds no data, keeps no books and knows nothing about what is being sold — it is told "pulse the drawer on COM3" and it does that.

That is why it can be installed by whoever sets up the shop rather than by anybody with a password. It has no access to the company's figures because it never sees them.

## It listens only to this machine

The agent binds to the loopback interface and nothing else, and that is not configurable. A bridge reachable from the network is a cash drawer anybody on that network can open, and no deployment is worth that.

It also answers only the pages named in its configuration. A bridge that answered any page would let a browser tab from anywhere on the internet open the till's drawer while the shop was serving somebody.

## It says which till it is

Every response carries the station code, and a request naming a different till is refused rather than obeyed.

That check matters more than it sounds. Two tills on one counter, a browser tab left open from yesterday, a configuration copied from the next shop — any of them ends with one till's browser opening another till's drawer, and nobody diagnoses that quickly.

## It simulates until told not to

Out of the box the agent records what it would have sent and answers plausibly. Nothing is driven.

That is for the shop being set up, the developer building a till screen and the engineer reproducing a fault, all of whom need the software to run without a cash drawer on the desk. **Every response says `simulated: true`**, because a demonstration that looks exactly like the real thing is one somebody will believe on a day it matters.

Set `Simulate` to false once the ports are known to be right.

## Setting it up

Install it on the till, then set four things in `appsettings.json`:

- `StationCode` — which till this is. It must match the code in ASAP.
- `AllowedOrigins` — the address of the ASAP client, so the agent knows which page may call it.
- `Simulate` — false once the ports are right.
- `BaudRate` — the speed the devices expect, usually 9600.

Then check it: open `http://localhost:8731/health` on the till itself. It answers with the station code and whether it is simulating.

## The devices

**Cash drawer.** A drawer has no intelligence and no cable of its own: it is wired into the receipt printer and opened by a pulse on one of two pins. Which is why a drawer is set up against the *printer's* port, and why "the drawer will not open" is almost always a printer problem.

**Customer display.** Cleared and rewritten each time. Lines longer than the display are cut rather than wrapped — a two-line display that wraps shows the second half of the first line where the total should be, and a customer reading a total with somebody else's digits on the end is worse served than one reading a shortened product name.

**Scale.** Asked for a reading and sent back a line like `ST,GS, 1.234kg`. Scales differ, so the agent looks for the first number it can read and for the words that mean settled, rather than pretending to be a driver for one particular scale. If nothing can be read, what the scale actually said comes back with the refusal — that reply is usually the only clue to which scale it is.

Whether a reading has settled is **reported, not enforced**. A shop weighing something that will not settle — a live fish, a shaking hand — still has to sell it, and software that simply refuses is software worked around with a calculator.
