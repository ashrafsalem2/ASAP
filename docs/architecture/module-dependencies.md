# How modules depend on each other

Modules form a directed acyclic graph. A module may depend on modules below it, declares every
such dependency in `DependsOn`, and may never take part in a cycle.

That is the whole rule. What follows is why it is that rather than something stricter, because the
stricter version is the one people assume and it is wrong.

---

## Siblings do not reference each other

Inventory does not reference Finance. Finance does not reference Inventory.

Neither is above the other. A company can buy Inventory without Finance — a warehouse tracking
stock for a parent company that keeps its own books — and can buy Finance without Inventory, which
is most service businesses. Making either reference the other would make that impossible, and
would create the cycle the resolver exists to refuse.

They still have to trade. Stock movements have a value, and value belongs in the general ledger.
So the contract lives in the kernel, which both already depend on:

```
Inventory ──raises──▶ LedgerPostingRequested ◀──handles── Finance
                      (ASAP.Platform.Kernel.Accounting)
```

Inventory raises the event knowing only the account numbers on the item's category. Finance
handles it knowing only that something asked for a balanced set of lines. Remove Finance and the
event goes unhandled, which is exactly right for a company that does not own it.

## Modules above may depend on modules below

Purchasing is not a sibling of Inventory and Finance. It sits on top of both: a purchase receipt
*is* a stock movement, and a purchase invoice *is* a vendor ledger entry. There is no sense in
which a company could own Purchasing and not the things it posts into.

So Purchasing references both, and says so:

```csharp
public IReadOnlyCollection<string> DependsOn =>
[
    PlatformModule.Id,
    InventoryModule.Id,
    FinanceModule.Id,
];
```

This is a real dependency, not a workaround. Routing it through kernel events instead would buy
nothing — the coupling would still exist, it would simply be invisible to the resolver, to the
licence check and to anybody reading the code. An event is the right tool when two modules must
not know about each other. It is the wrong tool when one legitimately does.

## The rule is enforced, not trusted

`DependsOn` drives load order and licence gating, so a module that references another without
declaring it will load in the wrong order the first time the order matters — and it will work
until then, which is the worst kind of bug to leave lying around.

`ASAP.Conformance.Tests` therefore checks the two halves against each other: every project
reference between modules must have a matching `DependsOn`, and every declared `DependsOn` must
name a module that exists. The resolver already refuses cycles at startup.

---

## Deciding where something belongs

Ask which module could be sold without the other.

- **Both ways** — they are siblings. Put the contract in the kernel and let each side raise or
  handle it. Inventory and Finance.
- **One way only** — the one that cannot stand alone depends on the one that can, and says so.
  Purchasing depends on Inventory.
- **Neither** — it is one module that has been split for the wrong reason. Put it back together.
