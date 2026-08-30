# What an extension author may rely on

An extension is compiled against ASAP and loaded into it. That only works if there is a line between what will not change and what might, and the line has to be stated rather than implied — an author who guesses wrong finds out when a customer upgrades.

## The contract is the kernel

Everything in `ASAP.Platform.Kernel` is the contract: `IAsapModule`, `Result`, `MessageDefinition`, `PermissionDescriptor`, `SetupDescriptor`, `NavigationItem`, the CQRS interfaces, the clock, the tenancy and security contexts.

Within a major version:

- **Nothing is removed and nothing changes meaning.** A method returning a `Result` will not start throwing; a property that means one thing will not come to mean another.
- **Members may be added, including to interfaces.** That is why every optional member of `IAsapModule` has a default implementation — a new one does not break an extension that never heard of it.
- **New message codes, permissions and settings may appear.** Yours are prefixed with your module id, so they cannot collide with them.

## What is not the contract

`ASAP.Platform.Core`, `ASAP.Platform.Persistence`, and every `ASAP.Modules.*` assembly.

You may reference them, and they will work. They change between minor versions and you will have to recompile, sometimes edit. That is a deliberate trade: freezing them would freeze ASAP.

**If you find yourself needing something from them that the kernel does not offer, that is worth reporting.** It usually means the kernel is missing something rather than that you are doing something unusual — the same has been true of ASAP's own modules more than once.

## Versions

The manifest declares what you built against:

```json
{ "platformVersion": "1.0" }
```

An extension declaring `1.0` loads on any `1.x`. On `2.0` it is **refused rather than loaded and hoped for**.

That refusal is the important part. An extension that half works writes half-correct figures into somebody's books, and a wrong number that arrived quietly is worse than a system that would not start — the second is found in a minute and the first at an audit.

## What a major version means

We will change the major version when something in the kernel has to be removed or has to change meaning. When that happens:

- Every removal is listed with what replaces it.
- The version before it marks the removals as obsolete first, so a recompile warns you before an upgrade breaks you.
- Extensions declaring the old version keep working on the old one. Nobody is forced to move on somebody else's schedule.

## Check before you ship

The SDK ships the same conformance rules ASAP applies to its own modules:

```csharp
[Fact]
public void The_extension_conforms()
    => ExtensionCheck.ThrowIfNotConforming(new WarrantyExtension());
```

These are not house style. A refusal that does not say what to do about it leaves somebody stuck; a message with no Arabic gives half your users English; a menu entry needing a permission nothing declares is invisible to everybody including the administrator. Each is a real fault that has happened in this codebase and is now caught mechanically.

Run it in your own test suite and find out in a second, rather than from a customer.

## The worked sample

`samples/Acme.Warranty` is a complete extension: permissions, a setting, bilingual messages with resolutions, a menu entry, a registered service, a manifest and its tests. It is built in this repository against the SDK project rather than a published package, so it cannot go stale the first time the SDK changes.
