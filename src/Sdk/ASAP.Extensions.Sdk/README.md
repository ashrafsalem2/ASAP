# ASAP Extension SDK

Write an extension for ASAP ERP.

An extension is declared exactly the way every module ASAP ships is declared. That is not a coincidence and it is the whole point: an extension is not a lesser kind of thing bolted to the side, so anything Finance or Inventory can do, yours can do too — declare permissions, add settings, put entries in the menu, raise messages in both languages, handle events, register services.

## The smallest extension that does something

```csharp
using ASAP.Extensions.Sdk;
using ASAP.Platform.Kernel.Messaging;
using ASAP.Platform.Kernel.Security;

public sealed class WarrantyExtension : AsapExtension
{
    public override string ModuleId => "Acme.Warranty";

    public override LocalizedText DisplayName => new("Warranty tracking", "تتبع الضمان");

    public override string Publisher => "Acme Software";

    public override IReadOnlyCollection<PermissionDescriptor> Permissions =>
    [
        PermissionDescriptor.Define(
            ModuleId, "Warranty", PermissionAction.Read,
            new LocalizedText("View warranties", "عرض الضمانات")),
    ];
}
```

`AsapExtension` adds no power over implementing `IAsapModule` yourself. It exists because the interface has fourteen members and a first extension needs four, and a page of empty overrides between you and the thing you came to write teaches you nothing.

## Check your own declarations

ASAP holds its own modules to rules a conformance test enforces. They are not house taste — they are what makes a refusal answerable and a menu honest — and an extension that ignores them serves its users worse than the rest of the system does.

So the checks ship with the SDK:

```csharp
[Fact]
public void The_extension_conforms()
    => ExtensionCheck.ThrowIfNotConforming(new WarrantyExtension());
```

It reports every problem at once, in words:

- a message that blocks somebody without saying what to do about it
- a message with no Arabic, or one that drops a placeholder in translation
- a permission implying one nothing declares
- a setting or menu entry needing a permission nothing declares
- anything not prefixed with your module id, which could collide with another extension's

## The manifest

Beside your assembly, in `asap-extension.json`:

```json
{
  "id": "Acme.Warranty",
  "name": "Warranty tracking",
  "version": "1.0.0",
  "publisher": "Acme Software",
  "assembly": "Acme.Warranty.dll",
  "platformVersion": "1.0",
  "requires": [ "Sales" ]
}
```

`platformVersion` is the ASAP version you built against. `requires` names modules yours needs: the load order is worked out from it, and an extension whose dependency is missing is refused at startup rather than failing later on the one screen that needed it.

## What you may rely on

Everything in `ASAP.Platform.Kernel` is the contract. Within a major version:

- **Nothing is removed and nothing changes meaning.** A method that returns a `Result` today will not start throwing.
- **Members may be added**, including to interfaces — which is why every optional member of `IAsapModule` has a default implementation, so a new one does not break you.
- **New message codes, permissions and settings may appear.** Yours are prefixed with your module id, so they cannot collide.

What is **not** contract: anything in `ASAP.Platform.Core`, `ASAP.Platform.Persistence`, or any `ASAP.Modules.*` assembly. You may reference them and they will work, but they change between minor versions. If you find yourself needing something from them that you cannot get from the kernel, that is worth reporting — it usually means the kernel is missing something.

## Upgrading

An extension declaring `"platformVersion": "1.0"` loads on any 1.x. On a major version it is refused rather than loaded and hoped for: an extension that half works writes half-correct figures into somebody's books, which is worse than one that does not start.

Symbols and sources are in the package, so you can step into ASAP's own code rather than guess at it.
