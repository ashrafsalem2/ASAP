# Shipments

A shipment is stock leaving; an invoice is money owed. They are separate because they happen at different moments and sometimes to different degrees.

## Partial shipping

An order can ship in parts. Each shipment takes the stock it actually sent, and the order remembers what is outstanding. Shipping more than was ordered is refused — the difference is a decision somebody should make on the order rather than discover in the warehouse.

## Shipping and invoicing in either order

Goods can go before the invoice or after it. What must not happen is either being counted twice, so each records what it has done and neither will do it again.

Where goods go first, the cost leaves at the moment they do. That is why a sale before the purchase invoice arrives leaves an estimated cost — see the inventory topics on negative stock and cost adjustment.

## Location

A shipment names the location the goods left from, and that decides which branch the sale is reported against. Getting it wrong does not fail; it quietly reports one shop's sale against another.
