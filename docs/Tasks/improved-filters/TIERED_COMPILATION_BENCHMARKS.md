# Tiered Compilation Benchmarks

Date: 2026-06-06.

Command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Environment:

- Runtime: .NET 10.0.8
- OS: Windows NT 10.0.26200.0
- Process: x64
- Samples: 9

## Layer 3 Results

Focused filter rows:

| Scenario | Manual ns | Engine ns | Extra ns | Alloc/op |
|---|---:|---:|---:|---:|
| Hardcoded 4-clause scalar filter | 0.48 | n/a | n/a | 0 B |
| Immediate expression-compiled 4-clause scalar filter | 0.48 | 2.09 | 1.61 | 0 B |
| Tiered interpreted 4-clause scalar filter | 0.48 | 12.86 | 12.37 | 0 B |
| Tiered promoted 4-clause scalar filter | 0.48 | 4.43 | 3.95 | 0 B |
| Immediate uncached 4-clause registration | 14.00 | 291,686.00 | 291,672.00 | 11,499.4 B |
| Tiered cold 4-clause registration | 12.50 | 3,416.50 | 3,404.00 | 5,296.2 B |

The benchmark-only uncached registration helper now bypasses the structural
kernel cache. That makes the registration rows measure first registration work
rather than cache hits.

## Interpretation

Tiered cold registration avoids immediate `Expression.Compile` and drops the
measured first-registration cost from about 292 us to about 3.4 us. It also
avoids roughly 6.3 KB of allocation in this case before any background
promotion happens.

Tiered interpreted evaluation is intentionally slower than the hot compiled
path. For the measured 4-clause scalar filter it costs about 12.86 ns versus
2.09 ns for immediate expression compilation.

Tiered promoted evaluation is closer to immediate compilation, but still has a
stable holder hop and a status read: 4.43 ns versus 2.09 ns. The Layer 3 change
keeps Tier 0 counters off the Tier 1 hot path, so promoted filters do not pay
per-evaluation `Interlocked` counter updates.

Approximate promotion break-even versus staying interpreted:

```text
Expression compile cost / (interpreted ns - promoted ns)
291,686.00 ns / (12.86 ns - 4.43 ns) = about 34,600 evaluations
```

The default thresholds remain deliberately above that measured break-even:

| Shape | Default minimum evaluations |
|---|---:|
| Complex filters with arrays or large `in` lists | 50,000 |
| Multi-clause scalar filters | 75,000 |
| Simple scalar filters | 100,000 |

These thresholds avoid compiling one-off filters, keep the compile work off the
dispatch thread, and still promote filters that are likely to recover the
background compile cost through future interpreted-evaluation savings.

## Layer 4 Projection Skeleton

Focused projection rows from the Layer 4 run:

| Scenario | Manual ns | Engine ns | Extra ns | Alloc/op |
|---|---:|---:|---:|---:|
| Immediate 2-field projection | 32.64 | 33.51 | 0.87 | 312 B |
| Tiered interpreted 2-field projection | 32.07 | 38.74 | 6.68 | 312 B |

Layer 4 intentionally starts tiered projections on the interpreted field-loop
path and counts materializations/direct payload writes. The measured skeleton
cost is about 5.8 ns over the immediate composed projector for this 2-field
case, with no additional allocation. Layer 5 should recover most of that delta
by promoting hot projections to composed field projectors and direct writer
helpers off-thread.

## Layer 5 Projection Promotion

Focused projection rows from the Layer 5 run:

| Scenario | Manual ns | Engine ns | Extra ns | Alloc/op |
|---|---:|---:|---:|---:|
| Immediate 2-field projection | 28.17 | 33.07 | 4.90 | 312 B |
| Tiered interpreted 2-field projection | 28.39 | 43.49 | 15.10 | 312 B |
| Tiered promoted 2-field projection | 28.90 | 34.79 | 5.89 | 312 B |

Off-thread projection promotion recovers most of the interpreted skeleton cost:
the 2-field tiered row drops from 43.49 ns interpreted to 34.79 ns after the
composed field projector is published. That leaves about 1.7 ns over the
immediate composed projector for the stable tier holder.

Payload writes are counted as hotness signals and keep using the existing direct
payload writer so bytes stay identical. A later direct-writer sourcegen/runtime
helper can use the same tier state without changing projection registration or
subscription grouping.

## Layer 6 Hot Manifest Sink

Layer 6 adds optional hot filter/projection manifest recording. The benchmark
suite runs without a manifest sink attached, so these rows mainly verify that
the no-sink hot path stays stable:

| Scenario | Engine ns |
|---|---:|
| Tiered promoted 4-clause filter | 4.41 |
| Tiered interpreted 4-clause filter | 12.78 |
| Tiered promoted 2-field projection | 36.40 |
| Tiered interpreted 2-field projection | 44.24 |

The manifest work happens only when a sink is supplied in compiler options and
only when a tiered entry crosses the promotion threshold. The writer coalesces
reports off-thread and atomically replaces the JSON manifest.

## Layer 7 Precompiled Provider Lookup

Layer 7 adds a provider registry that is inactive unless a provider is
registered. The benchmark suite exercises the default no-provider path:

| Scenario | Engine ns |
|---|---:|
| Immediate 1-field filter | 1.99 |
| Tiered promoted 4-clause filter | 4.87 |
| Tiered promoted 2-field projection | 34.92 |
| Tiered interpreted 2-field projection | 41.27 |

Provider tests verify that registered precompiled filters/projections beat both
tiered and immediate fallback. Provider hits are returned directly and are not
stored in the ordinary runtime cache, so removing a provider does not leave a
provider delegate behind in the structural cache.

## Layer 8 Hot DLL Source Generator

Layer 8 adds a JSON-driven source generator path. Manifest files named
`fourstory-filter-hot-manifest.json` or `*.fourstory-hot.json` are consumed as
Roslyn additional files. Valid entries emit an auto-registering
`IPrecompiledTieredProvider`; stale schema/engine/generator versions emit
diagnostics and no provider source.

The generated provider test compiles and loads a hot DLL, then verifies both
`FilterCompiler` and `ProjectionCompiler` hit the precompiled provider before
tiered fallback. The benchmark suite still measures the default no-provider
runtime path:

| Scenario | Engine ns |
|---|---:|
| Immediate 1-field filter | 1.96 |
| Immediate 4-clause filter | 2.09 |
| Tiered interpreted 4-clause filter | 13.71 |
| Tiered promoted 4-clause filter | 4.40 |
| Tiered interpreted 2-field projection | 44.78 |
| Tiered promoted 2-field projection | 34.80 |

The sourcegen layer does not change dispatch behavior when no hot DLL is
registered. Its value is startup-side: frequent filters/projections can avoid
new runtime `Expression.Compile` work on the next process start once the server
loader supplies the generated DLL.

## Layer 9 Startup Loader

Layer 9 adds the server startup loader. Generated hot DLLs now carry assembly
metadata for the manifest hash, schema, filter engine, and generator version.
The loader reads that metadata through `PEReader` before loading the assembly,
compares it with the current manifest, validates runtime/version fields, then
executes the module initializer so the provider is registered before plugin
subscriptions are created.

Focused validation compiled a generated hot provider to disk, loaded it through
`HotTieredProviderLoader`, verified the provider beat tiered fallback, then
mutated the manifest and confirmed the stale hash was rejected. The benchmark
suite still measures the no-provider steady state:

| Scenario | Engine ns |
|---|---:|
| Immediate 1-field filter | 1.93 |
| Immediate 4-clause filter | 2.10 |
| Tiered interpreted 4-clause filter | 14.70 |
| Tiered promoted 4-clause filter | 4.08 |
| Tiered interpreted 2-field projection | 42.78 |
| Tiered promoted 2-field projection | 35.77 |

The startup loader has no dispatch-path cost after startup. When disabled or
when artifacts are missing/stale, the server logs the diagnostic and continues
with the normal interpreted/expression/tiered fallback paths.

## Layer 10 Runtime Batch Hooks

Layer 10 adds design hooks for long-running server batch compilation without
implementing the compiler yet. `RuntimeHotProviderBatchSink` can wrap the hot
manifest sink, collect newly hot filter/projection definitions, and enqueue a
whole batch through `IRuntimeHotProviderBatchQueue`. The future compiler hook is
`IRuntimeHotProviderBatchCompiler`; `Expression.Compile` remains the immediate
promotion fallback.

Focused tests verify that the batch sink forwards to the manifest sink, waits
for the configured minimum entry count, and queues complete batches off-thread.
The benchmark suite still measures the default path with no batch sink attached:

| Scenario | Engine ns |
|---|---:|
| Immediate 1-field filter | 1.93 |
| Immediate 4-clause filter | 2.09 |
| Tiered interpreted 4-clause filter | 13.43 |
| Tiered promoted 4-clause filter | 4.38 |
| Tiered interpreted 2-field projection | 42.18 |
| Tiered promoted 2-field projection | 34.79 |

The batch hook has no overhead unless a server opts into the sink. When enabled,
work is still triggered from promotion/hot-report paths, coalesced into batches,
and handed to the queue off-thread so dispatch remains on the existing tiered
runtime path.

## Layer 11 Direct Hot-Path Swaps And Typed Kernels

Layer 11 removes the stable tier-state hop from promoted filters and
projections. Filter promotion now publishes the compiled predicate into
`CompiledKernel` itself; projection promotion publishes no-include field
projectors into `CompiledProjection`. The tier state remains responsible for
cold interpretation, counters, promotion thresholds, hot-manifest recording,
and snapshots, but it is no longer called by the promoted hot path.

Expression compilation now produces a typed `Func<TSubject, bool>` plus an
object adapter. Existing object-based dispatch keeps working, while generic
call sites that have the concrete subject type can invoke the typed predicate
directly. This is runtime-only; no sourcegen was changed for this layer.

Focused rows from the benchmark run:

| Scenario | Manual ns | Engine ns | Extra ns | Alloc/op |
|---|---:|---:|---:|---:|
| Immediate 1-field filter | 0.45 | 2.38 | 1.93 | 0 B |
| Immediate 4-clause filter | 0.49 | 2.14 | 1.65 | 0 B |
| Tiered interpreted 4-clause filter | 0.51 | 13.41 | 12.90 | 0 B |
| Tiered promoted 4-clause filter | 0.53 | 2.18 | 1.64 | 0 B |
| Immediate 2-field projection | 32.93 | 36.89 | 3.96 | 312 B |
| Tiered interpreted 2-field projection | 35.62 | 53.11 | 17.50 | 312 B |
| Tiered promoted 2-field projection | 32.10 | 35.66 | 3.57 | 312 B |
| Immediate uncached 4-clause registration | 13.00 | 194,065.50 | 194,052.50 | 11,643.4 B |
| Tiered cold 4-clause registration | 11.50 | 2,148.00 | 2,136.50 | 5,432.2 B |

Promoted tiered filters now converge with immediate compiled filters in the
measured hot path: 2.18 ns versus 2.14 ns for the four-clause filter. That
removes the previous promoted-tier tradeoff where the stable holder/state hop
left promoted filters around 4.3-4.4 ns.

Promoted tiered projections are also back in the immediate projection range:
35.66 ns versus 36.89 ns in this sample. Interpreted tiered projections remain
slower, which is expected because they still count hotness and use the
field-projector loop until promotion.

The measured break-even versus staying interpreted is now lower because the
promoted path is cheaper:

```text
Expression compile cost / (interpreted ns - promoted ns)
194,065.50 ns / (13.41 ns - 2.18 ns) = about 17,300 evaluations
```

The default thresholds remain intentionally above this focused break-even so
single-use or short-lived filters stay interpreted and avoid unrecoverable
runtime compile memory. Hot filters still converge to immediate compiled
performance once promoted.

## Layer 12 Typed Buckets And Cached Matchers

Layer 12 reduces object dispatch in the common filter paths without changing
source generation. The server sidecar registry, client bridge registry, and
local projected-client-event registry now store subscriptions in typed per-event
buckets backed by `TypedFilterSubscriptionIndex<TSubscription,TSubject>`.
Exact index key extraction compiles a typed field accessor once per indexed
field snapshot. Residual predicates use a cached
`CompiledKernelMatcher<TSubject>` so hot calls avoid the public
`CompiledKernel.Matches<TSubject>` delegate type check.
The matcher also treats the singleton `CompiledKernel.Any` as a typed
`static _ => true` predicate, while keeping other broad/non-selective kernels
on their real predicate.

The old object-based `FilterSubscriptionIndex<TSubscription>` remains for
dynamic fallback callers, but the bridge/server event dispatch paths now cast
once at the bucket boundary and stay typed through candidate lookup and
residual matching. Tiered matchers keep watching for promotion while cold and
freeze once they observe the final compiled tier.

Focused rows from the benchmark run:

| Scenario | Manual ns | Engine ns | Extra ns | Alloc/op |
|---|---:|---:|---:|---:|
| Immediate 1-field filter | 0.45 | 1.55 | 1.10 | 0 B |
| Immediate 4-clause filter | 0.48 | 1.86 | 1.39 | 0 B |
| Tiered interpreted 4-clause filter | 0.47 | 16.54 | 16.07 | 0 B |
| Tiered promoted 4-clause filter | 0.47 | 2.49 | 2.01 | 0 B |
| `in` + 2 arrays | 5.16 | 5.02 | -0.13 | 0 B |
| 32-value `in` | 1.43 | 4.30 | 2.88 | 0 B |
| 256 exact scalar scan | 4.12 | 1,813.99 | 1,809.87 | 0 B |
| 256 exact scalar indexed subscriptions | 1.64 | 34.23 | 32.58 | 0 B |
| Server projected dispatch pipeline | 1,789.01 | 1,871.25 | 82.24 | -40 B |
| Client projected dispatch pipeline | 1,014.71 | 1,276.26 | 261.56 | -48 B |
| Filter + 2 fields | 33.78 | 38.30 | 4.52 | 312 B |

Compared with Layer 11, the direct filter rows improve materially. Indexed
dispatch is still faster than the Layer 11 object-index path in this sample;
projected dispatch is closer to runtime noise because projection payload work
dominates after filtering:

| Scenario | Layer 11 engine ns | Layer 12 engine ns |
|---|---:|---:|
| Immediate 1-field filter | 2.38 | 1.55 |
| Immediate 4-clause filter | 2.14 | 1.86 |
| `in` + 2 arrays | 5.94 | 5.02 |
| 32-value `in` | 5.00 | 4.30 |
| Indexed 256-subscription dispatch | 41.10 | 34.23 |
| Server projected pipeline | 1,879.30 | 1,871.25 |
| Client projected pipeline | 1,221.45 | 1,276.26 |

The only remaining hot filter row that is clearly slower than immediate is
tiered interpreted, which is expected: it deliberately keeps the cold path in
the interpreter and still pays tier counters until promotion. Promoted tiered
filters are close to immediate again after the matcher freezes its promotion
version tracking.

The runtime still keeps object APIs for dynamic IPC/projection surfaces and for
fallback field getters. The common bridge/server filtering path, however, no
longer needs pre-boxed event objects, object-index key extraction, or
per-evaluation typed-delegate discovery.
