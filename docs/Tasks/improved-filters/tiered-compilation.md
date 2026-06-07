# Tiered Filter And Projection Compilation Plan

Date: 2026-06-06.

## Goal

Avoid paying `Expression.Compile` and permanent dynamic-method memory for
single-use or very cold filters/projections, while preserving the current fast
compiled hot path for frequently evaluated subscriptions.

The runtime should:

- register new filters/projections immediately using a validated interpreted
  representation;
- promote hot entries by compiling expressions off-thread and swapping the
  active delegate in place;
- record frequent filters/projections into a persisted JSON manifest;
- generate a precompiled hot-filter DLL from that JSON for the next server
  start;
- load that DLL on startup and seed the runtime cache before normal plugin
  subscriptions begin.

## Non-Goals

- Do not replace the current structural cache.
- Do not compile a Roslyn DLL for every newly registered filter.
- Do not block event dispatch on background compilation.
- Do not remove fallback support for safe plugin-owned DTOs.
- Do not make plugin code or sidecars aware of tiering.

## Runtime Tiers

### Tier 0: Interpreted

New filters and projections start in interpreted mode unless a matching
precompiled or already compiled entry is available.

For filters, Tier 0 should use a compact validated IR with typed scalar and
array opcodes where possible. It must avoid reflection and object traversal on
the hot path. Schema discovery still happens at registration time and produces
cached field access metadata.

For projections, Tier 0 should use compiled schema field accessors without
building a whole expression delegate. It may keep the current field loop and
direct payload writer, but the projection registration path should avoid
`Expression.Compile` for short-lived projections.

Tier 0 must be deterministic. Validation errors happen synchronously during
registration, not later during promotion.

### Tier 1: Expression Compiled

When a Tier 0 entry becomes hot, enqueue off-thread expression compilation.

The background compiler builds the same optimized whole-filter predicate and
composed projection delegates currently used by the hot path. When compilation
finishes, the runtime atomically swaps the active delegate/reference from the
interpreter to the compiled implementation.

If the subscription is removed, the schema version changes, or the entry has
already been replaced by a newer generation, discard the compiled result.

### Tier 2: Precompiled Hot DLL

A server-local hot-filter DLL is generated from a persisted JSON manifest. This
DLL is loaded on the next server start and registers strongly typed compiled
filters/projections before plugins subscribe.

Tier 2 is profile-guided precompilation. It optimizes startup and recurring
frequent filters; it is not the immediate fallback for one-off runtime filters.

## Filter Plan

### Filter Registration

1. Normalize the incoming `ServerFilterExpression`.
2. Build a structural fingerprint using the existing cache key logic.
3. Check the in-memory compiled cache.
4. Check registered precompiled DLL providers.
5. If no compiled provider exists, build a Tier 0 interpreted filter.
6. Register a `TieredCompiledKernel` wrapper with:
   - subject type;
   - fingerprint;
   - schema version;
   - interpreted evaluator;
   - volatile active evaluator;
   - counters;
   - compile state.

The wrapper still exposes the same `CompiledKernel` behavior to callers.

### Filter Evaluation Counters

Track:

- evaluation count;
- match count;
- first seen timestamp;
- last seen timestamp;
- promotion state;
- failed promotion count.

Avoid a contended atomic increment on every event if benchmarks show overhead.
Use one of:

- sampled increments, for example count every 16th evaluation;
- per-thread counters flushed periodically;
- relaxed `Interlocked` only for Tier 0 entries.

Tier 1 and Tier 2 entries do not need frequent counter updates except for hot
manifest refresh.

### Filter Promotion

Promotion criteria should require both time and usage. Use evaluation count as
the primary signal, not match count. A filter that rejects every event can still
be hot enough to justify compilation.

The current measured break-even is roughly:

- first uncached whole-filter `Expression.Compile`: about 204 us for the
  measured four-clause filter;
- compiled complex filter versus compact interpreter: about 6.2 ns saved per
  evaluation;
- break-even for that complex shape: about 33,000 evaluations;
- simple filters may save closer to 2 ns per evaluation, pushing break-even
  toward about 100,000 evaluations.

Default criteria should therefore be:

- minimum age: 5-30 seconds;
- minimum evaluations: 50,000-100,000;
- optional match count threshold for projected dispatch;
- no previous compile failure for the same generation.

Suggested starting thresholds:

| Shape | Minimum evaluations |
|---|---:|
| Complex filters with arrays, `in`, or many clauses | 50,000 |
| Multi-clause scalar filters | 75,000 |
| Simple scalar filters | 100,000 |

Tune these thresholds from production metrics after tiering is measured.

When criteria pass:

1. Mark state as `CompilationQueued`.
2. Push a compile job to a bounded background queue.
3. Background worker compiles the expression.
4. Worker publishes result with `Volatile.Write` or `Interlocked.CompareExchange`.
5. State becomes `Compiled`.

Compilation concurrency should be limited, likely 1-2 workers per process.

### Filter Interpreter

The interpreter should not be the old boxed fallback. It should execute a
validated opcode stream such as:

- `And`, `Or`, `Not`;
- scalar comparison;
- `In`;
- `Exists`;
- array `Contains`;
- constant event type/name checks;
- approved intrinsic calls when available.

Each field opcode should carry resolved `FilterField` access metadata, not
property names that require lookup per event.

The interpreter is allowed to be slower than compiled expressions because it is
only Tier 0, but it should stay allocation-free after registration.

## Projection Plan

### Projection Registration

1. Normalize `EventProjectionExpression`.
2. Fingerprint projection fields, aliases, includes, and include arguments.
3. Check in-memory compiled projection cache.
4. Check precompiled DLL providers.
5. If no provider exists, create Tier 0 projection.
6. Register a tiered projection wrapper with:
   - field projectors;
   - include projectors;
   - direct payload writer path;
   - counters;
   - promotion state.

### Projection Tier 0

Tier 0 projection should use the existing validated schema field accessors and
include projectors. It should avoid generating expression delegates for
short-lived projections.

For direct IPC payloads, Tier 0 can still use `ProjectedPayloadWriter`; the
writer should consume the active projection implementation through a stable
interface.

### Projection Promotion

Promote projections after enough projected deliveries, not just subscriptions.

Good promotion signals:

- projection materialization count;
- direct payload write count;
- include count;
- total projected fields written;
- measured or estimated payload cost.

Off-thread promotion can compile:

- composed no-include field-array projectors;
- field-shape-specific direct payload writers;
- future typed include wrappers if they remain synchronous and safe.

Swap the active projector/writer in place the same way as filters.

## In-Place Delegate Switch

Use a stable holder object so subscriptions do not need to be re-registered.

Conceptual shape:

```csharp
internal sealed class TieredKernelState
{
    private Func<object, bool> _current;

    public bool Matches(object subject) => Volatile.Read(ref _current)(subject);

    public void Promote(Func<object, bool> compiled)
    {
        Volatile.Write(ref _current, compiled);
    }
}
```

The real implementation should also track generation, dispose/unregister state,
and compile status.

## Background Compiler

Add a shared background compiler service for filters and projections.

Responsibilities:

- bounded queue;
- cancellation on shutdown;
- compile concurrency limit;
- stale job detection;
- metrics for queued, compiled, discarded, failed;
- failure logging with fingerprint and schema version;
- no blocking on event dispatch threads.

Compilation failures should not break active subscriptions. Keep the Tier 0
interpreter active and mark the generation as failed to avoid retry storms.

## Hot Manifest JSON

Persist frequently used filters/projections to a server-local manifest.

Example shape:

```json
{
  "schema": "fourstory.filters.hot.v1",
  "runtimeVersion": "10.0",
  "filterEngineVersion": "v1",
  "generatedAtUtc": "2026-06-06T00:00:00Z",
  "entries": [
    {
      "kind": "filter",
      "subjectType": "FourStory.Plugin.Abstractions.Server.Events.ItemUsedEvent, FourStory.Plugin.Abstractions",
      "schemaVersion": "generated-schema-v1",
      "fingerprint": "sha256...",
      "templateFingerprint": "sha256...",
      "expression": {},
      "observed": {
        "evaluations": 12500000,
        "matches": 200000,
        "firstSeenUtc": "2026-06-06T10:00:00Z",
        "lastSeenUtc": "2026-06-06T12:00:00Z"
      }
    }
  ]
}
```

The exact expression serialization can reuse existing filter/projection DTOs.
Do not persist live delegates, reflection objects, or runtime-only state.

### Manifest Update Policy

Update the manifest off-thread.

Rules:

- only write entries that pass frequency thresholds;
- coalesce writes;
- write through a temp file and atomic replace;
- keep a maximum entry count;
- decay or remove stale entries;
- include enough version data to invalidate safely.

Potential thresholds:

- minimum evaluations: 100,000;
- minimum projected deliveries: 10,000;
- seen in at least two server sessions, optional;
- last seen within a retention window.

## Hot DLL Generation

Add a source generator or generator-backed build project that reads the JSON
manifest and emits C# provider code.

The generated DLL should expose a provider contract, for example:

```csharp
public interface IPrecompiledFilterProvider
{
    bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? predicate);
    bool TryGetProjection(Type subjectType, string fingerprint, out object? projection);
}
```

Generated code should prefer template methods where possible:

```csharp
private static bool Match_ItemUsed_ItemId(object subject, FilterParameters p)
{
    var item = (ItemUsedEvent)subject;
    return item.ItemId == p.Int64_0;
}
```

This lets many filters with the same shape and different constants share a
method and use a small immutable parameter object.

### Build Flow

1. Runtime updates `hot-filters.json`.
2. Async job triggers a build of the hot-filter project.
3. Source generator reads the JSON.
4. Generated provider source is compiled into a DLL.
5. DLL and manifest hash are stored under server runtime artifacts.
6. The current process does not need to load it immediately in phase one.
7. Next server start loads it if validation succeeds.

### Startup Load Flow

1. Locate hot-filter DLL and manifest.
2. Validate:
   - manifest schema;
   - runtime version;
   - filter engine version;
   - schema version;
   - generator version;
   - subject type availability;
   - manifest hash.
3. Load assembly.
4. Instantiate provider.
5. Register provider with filter/projection compiler.
6. Seed in-memory cache as subscriptions register.

Invalid DLLs should be ignored with diagnostics, not crash startup.

## Runtime Temporary DLLs

Runtime temporary DLL compilation is phase two.

Use it only for batching many newly hot filters/projections during long server
uptime. Do not compile one DLL per filter.

If implemented:

- compile batches asynchronously;
- load into a collectible `AssemblyLoadContext`;
- track delegates so assemblies can unload;
- discard temp DLLs after their entries are folded into the main hot manifest;
- keep `Expression.Compile` as immediate fallback.

## Invalidation

Invalidate interpreted, compiled, and precompiled entries when any of these
change:

- filter engine version;
- projection wire shape version;
- schema provider version;
- subject type assembly version or MVID;
- field access path availability;
- intrinsic provider version;
- include provider version;
- content/runtime version if an intrinsic depends on content facts.

Precompiled DLL validation must be stricter than runtime expression compilation
because it can survive across server restarts.

## Metrics

Expose counters:

- Tier 0 evaluations;
- Tier 1 evaluations;
- Tier 2 evaluations;
- promotion queued/completed/discarded/failed;
- compile queue length;
- average compile time;
- hot manifest entries added/removed;
- precompiled provider hit/miss;
- stale DLL rejection reason.

These metrics decide whether tiering and hot DLLs are actually worth their
complexity.

## Tests

### Filter Tests

- New filter starts interpreted.
- Cold filter never queues compilation.
- Hot filter queues compilation after age and evaluation thresholds.
- Completed compilation swaps delegate in place.
- Removed subscription discards completed background compile.
- Compile failure leaves interpreter active.
- Equivalent filters share structural cache after promotion.
- Precompiled provider beats interpreter and expression fallback.

### Projection Tests

- New projection starts interpreted.
- Hot projection queues composed/direct writer compilation.
- Active projection swaps in place.
- Direct payload bytes stay identical before and after promotion.
- Includes run with the same semantics before and after promotion.

### Manifest Tests

- Hot entries are written after threshold.
- Cold entries are not written.
- Atomic write handles process interruption.
- Stale entries decay.
- Version mismatch invalidates entries.

### Hot DLL Tests

- JSON-driven generator emits provider for filter entries.
- Generated provider returns delegates for known fingerprints.
- Unknown fingerprints fall back.
- Startup loader rejects stale schema/runtime versions.
- Generated provider output matches current runtime compiler output.

### Concurrency Tests

- Promotion race with unsubscribe is safe.
- Multiple subscribers for the same fingerprint compile once.
- Queue limit prevents compile storms.
- Shutdown cancels queued jobs cleanly.

## Benchmarks

Add rows for:

- cold interpreted filter evaluation;
- promoted compiled filter evaluation;
- promotion compile cost off-thread;
- one-off registration with no compilation;
- current `Expression.Compile` registration;
- precompiled DLL provider lookup;
- precompiled DLL hot-path invocation;
- interpreted projection materialization;
- promoted projection materialization;
- direct payload write before/after promotion.

Compare against:

- handwritten hardcoded predicate/projection;
- current expression-compiled engine;
- compact opcode interpreter.

## Implementation Phases

### Phase 1: Tiered Runtime Skeleton

- Add tiered filter/projection holder types.
- Add interpreted mode for filters.
- Add counters and promotion state.
- Add fake background compiler in tests.
- Keep current compiler as the promotion target.

### Phase 2: Background Expression Compiler

- Add bounded compile queue.
- Add worker service.
- Add in-place delegate swap.
- Add stale generation checks.
- Add metrics.

### Phase 3: Projection Tiering

- Add interpreted projection wrapper.
- Promote composed field projectors and direct payload writers.
- Verify projected payload identity before/after promotion.

### Phase 4: Hot Manifest

- Add JSON manifest model.
- Add off-thread manifest updater.
- Add threshold and decay policy.
- Add atomic write.

### Phase 5: Hot DLL Generator

- Add provider contract.
- Add source generator/build project that reads JSON.
- Emit static typed filter/projection providers.
- Add startup loader and validation.

### Phase 6: Runtime Batch DLLs

- Optional.
- Compile hot batches into collectible temporary assemblies.
- Fold entries into the main manifest for next startup.

## Open Questions

- What should the default promotion threshold be for server event filters?
- Should projection promotion use delivery count, byte count, or both?
- Should hot manifests persist per server shard, per plugin set, or globally?
- How should plugin unload/reload affect hot manifest entries?
- Do we want generated DLLs signed or hash-validated only for local server use?
- Should the hot DLL be built by the running server process or by a separate
  maintenance/build process?

## Recommended First Slice

Implement phases 1 and 2 for filters only.

That directly addresses single-use `Expression.Compile` cost and permanent
dynamic-method memory without taking on the hot DLL build pipeline yet. Once
the tiered runtime is measured, projections and persisted hot DLL generation
can reuse the same promotion, profiling, and provider infrastructure.
