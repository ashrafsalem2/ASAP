# Till devices

A station is a named set of devices bound to a branch and a till. That binding is what lets a shop be set up once, and a broken till be swapped for a new one without anybody reconfiguring the software — the devices belong to the station, not to the computer.

## Most tills need nothing installed

The question worth answering first is what you actually have to put on the till, and for most shops it is nothing at all.

**Browser** — the till software reaches it as it stands. A receipt printer driven through the browser's own print dialog, and a barcode scanner, which almost always presents itself as a keyboard and simply types what it read. Between them that is a working shop, and neither needs anything installed or configured. A browser device does not even need an address: the print dialog asks the person standing at the till, which is the right place for that question.

**Network** — a device with its own address on the local network. A label printer, or a payment terminal. Nothing is installed on the till, but something has to be told where to find it.

**Bridge** — reached through a small program running on the till itself. A cash drawer, a customer display, a scale: things wired to a serial or USB port that no browser can open.

**Only the third case needs the agent.** Naming the connection per device is the point: a system that does not make this distinction ends up answering "install our agent" to a shop that needed nothing, and a five-minute setup becomes an IT project.

Each till can say what it needs. If nothing on it is a bridge device, nothing goes on that computer.

## Codes are per till

A device's code identifies it within its own till, not across the company. Two shops both calling their receipt printer `RCP` is ordinary, and treating it as a conflict would force every shop to invent a naming scheme for hardware that nobody outside that shop will ever refer to.

## Two of a kind

A counter may have two receipt printers — one for the customer and one for the kitchen. Mark one as the default and it is the one meant when nothing says otherwise.

Exactly one per kind per till. Marking a second as the default takes the flag off the first and says so, because two defaults answer the same question twice and the software would have to pick.

## Addresses

Free text, deliberately. What identifies a device differs by every kind of device there is — a host and port, a queue name, a serial port, a vendor's own identifier — and a scheme that tried to model all of them would need changing for the next one. Whatever drives the device knows how to read it; nothing else needs to.

## What is not here yet

**The bridge agent itself.** The device model knows which devices would need it and says so per till; the program that runs on the till and opens those ports is still to come. Until it exists, a bridge device can be recorded and planned for but not driven.

**Printing** already works without it — see [print templates](print-templates.md), which prints through the browser with no agent, no driver and no install.
