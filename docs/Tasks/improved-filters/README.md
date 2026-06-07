# Improved Server Plugin Filters

Status: design recommendation, no implementation yet.

Date: 2026-06-05.

## Problem

Server plugins can currently observe typed game events and register a small set
of mutation hooks. That is enough for specific cases, but the filtering model is
too narrow:

- `ServerLifecycleFilter` only supports `PluginId`, `RuntimeId`, and
  `ContentId`.
- Only the item/buff lifecycle helpers install parent-side sidecar filters.
- Generic sidecar event delivery still serializes and dispatches many events to
  every observing sidecar, even when a plugin does not care about them.
- Every new special filter shape currently means extending the server API.

The desired model is: plugins can express complex interest in any event or hook
without server changes for each new requirement, while the server keeps the hot
path cheap and never lets plugin-authored logic enumerate or mutate arbitrary
server state.

## Current State

Relevant existing pieces:

- `IServerEventsApi.Subscribe<TEvent>` forwards generic typed events through
  `GameEventBus`.
- `IServerEventsApi.OnBuffActivated`, `OnBuffDeactivated`, `OnItemAdded`, and
  `OnItemRemoved` accept `ServerLifecycleFilter` and are filtered parent-side
  for sidecars.
- `PluginHostService.OnGameEventAsync` still enqueues every event to trusted
  plugin fanout and forwards non-lifecycle events through
  `UntrustedSidecarFleet.DispatchGameEventAsync`.
- `UntrustedSidecarFleet.DispatchGameEventAsync` serializes one event payload
  and calls every active sidecar connection.
- Mutation hooks are explicit typed contexts under `MutationHookRegistry`.
  Sidecar hooks are bounded by the sidecar RPC timeout, but still pay one IPC
  round trip per invocation.

This means the current fast path exists only for a few lifecycle events. The
general observation path is still "send broadly, decide later".

## Recommendation

Build a custom restricted filter IR and compiler. Use C# expression/builder
syntax only as an authoring frontend, then lower to this IR before sending it to
the server.

Do not use Serialize.Linq, EventFilter, or DotNetIsolator as the primary
hot-path filter engine. They can inform or complement the design:

- EventFilter: useful operator vocabulary and JSON shape ideas.
- Serialize.Linq: possible SDK-side authoring/import helper after strict
  validation.
- DotNetIsolator: optional later tier for rare heavy code kernels after a cheap
  server-side prefilter matched.

The core server primitive should be a small, validated, indexable predicate over
a narrow fact snapshot, compiled once and reused.

## Proposed Model

Use "kernel" as the product term, but split it into two concrete categories:

1. Fast filter kernels: deterministic predicates over event or hook facts.
2. Mutation kernels: explicit typed hook handlers that may change a typed
   context, admitted only through existing trust/capability checks.

Fast filter kernels should decide whether an event/hook reaches a plugin. They
must not expose live server objects or perform mutations.

### Filter IR

The first version should support only:

- `and`, `or`, `not`
- `eq`, `ne`
- numeric/string comparisons where type-safe
- `in`
- `exists`
- bounded `any` over approved small arrays
- maybe `startsWith` for strings

Avoid arbitrary regex initially. If regex is added later, require bounded input,
compiled pattern caching, and strict limits.

Every field path must come from a generated allowlist for that event/hook fact
schema. Unknown fields are rejected at registration time. No runtime reflection,
no dynamic property lookup, no method calls, no constructors, no allocation-heavy
closures.

Example JSON-ish IR:

```json
{
  "kind": "and",
  "children": [
    { "field": "eventType", "op": "eq", "value": "DamageDealtEvent" },
    { "field": "damage", "op": "gte", "value": 500 },
    { "field": "damageKind", "op": "in", "value": ["skill", "drop"] }
  ]
}
```

Example SDK authoring shape:

```csharp
context.Server.Events.Subscribe(
    ServerKernel.For<DamageDealtEvent>()
        .Where(e => e.Damage >= 500)
        .Where(e => e.DamageKind.In("skill", "drop")),
    handler);
```

The SDK expression support should be a translator, not a remote C# execution
mechanism. If translation fails, plugin load should fail with a diagnostic that
names the unsupported expression node.

### Fact Snapshots

Create fact projections per event/hook kind. These should contain cheap scalar
fields needed for routing and predicates. Most current event DTOs already expose
many of them:

- event type / hook id
- plugin id / content id / runtime id
- character id / session id / map id
- item template id / skill id / monster template id
- damage kind / source / boolean result flags
- small arrays such as target ids or active buff skill ids, only where bounded

Do not predicate over full object graphs by default. Full DTO payloads are
serialized only after an event matches at least one sidecar subscription.

### Indexing

At registration time, split every predicate into:

- index keys: cheap exact-match facts such as event type, hook id, plugin id,
  content id, runtime id, map id, skill id, item template id, damage kind.
- residual predicate: the remaining compiled boolean function.

Dispatch should first select candidate subscriptions by event/hook kind and
available exact keys, then evaluate residual predicates only for candidates.

This turns the common case from:

```text
event -> serialize -> every sidecar -> sidecar filters
```

into:

```text
event -> extract facts -> indexed candidate set -> residual predicate -> serialize only matches
```

### Compilation And Caching

Compile each validated IR once and cache by:

```text
plugin id + event kind / hook id + filter hash + fact schema version
```

The compiler can emit either:

- a hand-interpreted opcode array for predictable safety and easy validation; or
- expression-tree generated delegates after validation.

Prefer the opcode interpreter first unless benchmarks prove it is a bottleneck.
It is simpler to audit and can still be fast when the candidate set is already
small. A later expression/delegate compiler can be an internal optimization
without changing the plugin-facing IR.

### Events

Add a parent-side subscription registry for all sidecar event subscriptions. The
sidecar should send a subscription request containing event kind plus filter IR.
The parent validates, indexes, and owns the filter.

Existing lifecycle helpers can lower to this:

```text
OnItemAdded(ServerLifecycleFilter(plugin, runtime, content))
  -> eventType == InventoryItemAddedEvent
  -> pluginId == plugin, when non-empty
  -> itemTemplateId == runtime, when non-zero
  -> contentId == content, when non-empty
```

Generic `Subscribe<TEvent>` should mean "all events of this type", not "all
events to every sidecar". That is still expressible as an indexed subscription
with only `eventType`.

### Mutation Hooks

Keep mutation as explicit hook APIs. Add optional filter IR to hook
registration:

```csharp
context.Server.MutationHooks.Register<CombatResultContext>(
    ServerMutationHookIds.CombatModifyResult,
    ServerKernel.For<CombatResultContext>()
        .Where(h => h.Skill.Id == 123)
        .Where(h => h.Victim.Kind == ObjectKind.Player),
    handler);
```

The server evaluates the fast filter before invoking a trusted in-process hook
or crossing into a sidecar. Hook context mutation remains limited to the typed
context. The filter cannot call server APIs.

This matters most for sidecars: it prevents an IPC call on every hot-path hook
invocation when a plugin only cares about a narrow subset.

## Option Evaluation

### Current Lifecycle Filter, Expanded Manually

Pros:

- Already works for item/buff lifecycle events.
- Very cheap per event.
- Safe because it is hard-coded.

Cons:

- Does not scale to arbitrary event/hook requirements.
- Keeps producing one-off API growth.
- Cannot express compound conditions like map + skill + damage kind.

Use only as compatibility syntax over the new IR.

### EventFilter

Local path: `D:\File\repos\work\Suxxesso\.source\EventFilter`.

Pros:

- Similar problem space: author filters, convert to JSON-ish shape, compile for
  in-memory evaluation.
- Operator vocabulary is close to what is needed.

Cons:

- Current implementation uses reflection, dynamic object access, runtime
  property filters, regex, and private-field inspection.
- Current code is more query-library than security boundary.
- Some paths log during evaluation and perform runtime member lookup.

Verdict: do not repurpose directly for production. Reuse ideas only.

### Serialize.Linq

Local path: `C:\Users\Jonas\repos\external\Serialize.Linq`.

Pros:

- Mature-ish expression serialization library.
- Convenient for SDK-side expression transport experiments.

Cons:

- General expression trees include method calls, constructor calls,
  invocation, member access, type tests, constants, and type resolution.
- Deserialization reconstructs `Expression` objects through type/member nodes.
- It is not a sandbox and not a policy model by itself.

Verdict: do not accept Serialize.Linq payloads as trusted server filter
definitions. If used at all, use it only before lowering to the restricted IR,
then validate the lowered form.

### DotNetIsolator

Local path: `C:\Users\Jonas\repos\external\DotNetIsolator`.

Pros:

- Stronger isolation model: Wasmtime/WebAssembly runtime, separate memory, no
  direct host disk/network access by default.
- Good fit for rare untrusted compute where plain filter IR is not expressive
  enough.
- Works in-process from the server's perspective while still sandboxing guest
  memory.

Cons:

- Experimental and explicitly not security-reviewed.
- Every call crosses the Wasm boundary and serializes values.
- Runtime creation has a .NET WASI startup cost, so runtimes must be kept warm.
- Pulls Wasmtime and a Wasm .NET runtime into the server process.
- Still needs a strict host callback allowlist.

Verdict: optional second tier, not the primary event filter path. Use only after
a cheap server IR prefilter selected a small number of calls.

### Trusted In-Process C# Delegates

Pros:

- Fastest possible evaluation.
- Easy for built-in/server-owner code.

Cons:

- Not isolated. AssemblyLoadContext is not a sandbox.
- Cannot restrict IO/reflection/host access once arbitrary plugin code runs.
- A bad delegate can block the gameplay path.

Verdict: acceptable only for built-in or server-owner-approved plugins, and even
then behind budgets and health degradation. Not for general untrusted sidecars.

### Custom Restricted DSL / IR

Pros:

- Small enough to audit.
- Can be validated before registration.
- Easy to index.
- Can be compiled and reused.
- Transport is stable across process/container boundaries.
- Works for any event/hook once fact schemas exist.

Cons:

- Must be designed and maintained.
- Plugin authors need SDK helpers and diagnostics.
- Complex logic beyond predicates needs a second-tier kernel or explicit hook.

Verdict: best primary path.

## Rough Overhead Estimates

These are budget estimates, not measured results from this branch.

| Path | Approximate cost once warm | Notes |
|---|---:|---|
| Exact index lookup only | < 50 ns to low 100s ns | Depends on dictionary/set shape and candidate count. |
| Small opcode/interpreter predicate | 50-500 ns | Good enough after indexing; predictable and auditable. |
| Validated compiled expression delegate | 10-200 ns | Faster but harder to audit than opcodes. |
| Current in-process mutation hook delegate | 100s ns plus context allocation | Existing ammo benchmark previously showed sub-us trusted hook cost. |
| Sidecar RPC hook/event call | 10s-100s us | Same-machine pipe/RPC/MessagePack; Docker can add more. |
| DotNetIsolator warm call | 10s-100s us or more | Wasm boundary plus serialization. Benchmark before hot-path use. |
| DotNetIsolator runtime startup | ms-scale+ | Keep warm; do not create per event. |

The performance target should be:

- mismatching sidecar subscriptions: no payload serialization and no IPC.
- matching sidecar event: one serialization plus one sidecar dispatch.
- matching mutation hook: one hook IPC only after prefilter match.

## Security Rules

The server-side filter evaluator must enforce:

- no IO;
- no reflection;
- no method calls;
- no arbitrary object traversal;
- no access to service providers or live server objects;
- bounded arrays and string lengths;
- bounded predicate node count/depth;
- deterministic execution with no clocks/random/global state;
- schema-versioned field allowlists;
- registration-time validation errors with plugin id and filter path.

DotNetIsolator host callbacks, if added later, must be separate from filters and
must be named, capability-gated, rate-limited, and explicitly allowlisted.

## Implementation Plan

1. Define `ServerFilterExpression` contracts in plugin abstractions. Keep them
   DTO-only and MessagePack-friendly.
2. Add fact schema definitions for the existing event DTOs and mutation
   contexts. Start with event/hook type, ids, and the scalar facts already
   present on DTOs.
3. Implement a validator that rejects unknown fields, type mismatches,
   unsupported operators, excessive depth, excessive array sizes, and
   non-indexable root shapes that would fan out too broadly.
4. Implement a small predicate interpreter over a fact reader.
5. Build a parent-side sidecar subscription registry and route sidecar generic
   event subscriptions through it.
6. Lower `ServerLifecycleFilter` helpers to the new registry.
7. Add filtered overloads for mutation hook registration and evaluate filters
   before sidecar hook RPC.
8. Add SDK builder/expression helpers that lower safe C# expressions to IR.
9. Add metrics: registered filters, candidate count, residual evaluations,
   matched dispatches, filtered-out dispatches, serialization count, IPC count,
   slow predicates, invalid registrations.
10. Benchmark with representative events and hooks before adding any Wasm
    kernel tier.

## Acceptance Criteria

- A sidecar subscribed to a specific event type no longer receives unrelated
  event types.
- A sidecar subscribed with an item/buff lifecycle filter behaves the same as
  today.
- A sidecar filter can combine at least event type, map/character ids, content
  ids, runtime ids, and a small set of event-specific scalar facts.
- Mismatching sidecar filters do not serialize the event payload.
- Mismatching sidecar mutation hook filters do not invoke sidecar RPC.
- Invalid filters fail during registration with actionable diagnostics.
- Filter evaluation is deterministic and allocation-free or near allocation-free
  in the steady state.
- Existing unfiltered event and mutation-hook APIs remain source-compatible.

## Open Questions

- How broad should v1 fact schemas be? A smaller schema is safer, but plugin
  authors may quickly need nested facts like `Skill.Id` or target object kind.
- Should expensive fields be lazily materialized only if a filter references
  them?
- Should registration require at least one high-selectivity index key for
  high-volume events like damage?
- Should untrusted sidecars be allowed to register mutation hooks at all, or
  only observe events plus call explicit server APIs?
- Should DotNetIsolator kernels be one runtime per plugin, per kernel, or a
  shared pool with plugin-owned assemblies?

## Decision

Proceed with the restricted IR + parent-side indexed subscription registry as
the primary path. Keep EventFilter and Serialize.Linq out of the server trust
boundary. Treat DotNetIsolator as a later optional sandbox for rare, heavy,
post-prefilter kernels.
