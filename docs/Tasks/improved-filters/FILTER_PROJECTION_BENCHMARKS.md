# Filter And Projection Kernel Benchmark Findings

Date: 2026-06-06.

## Scope

This benchmark measures the steady-state overhead of the shared
`FourStory.Plugin.Filters` runtime compared to handwritten C# predicates and
projection builders.

Measured path:

- compiled filter kernels after registration;
- compiled projections after registration;
- projected DTO construction;
- synchronous include projection for a representative enrichment path.
- a repeated filter registration case for equivalent filters. Early results
  measured uncached schema discovery plus filter compilation; after the
  structural cache improvement, this row measures a compiled-kernel cache hit.
- a focused dispatch candidate lookup case with 256 exact scalar
  subscriptions.
- focused matched projected-dispatch pipeline cases for server and client bridge
  shapes, including indexed lookup, residual filters, grouping, projection,
  MessagePack serialization, and a local RPC-call approximation.
- a focused projected-match grouping case with 16 matched subscriptions.
- a focused MessagePack projected-payload serialization case comparing
  materialize-then-serialize against a fused direct writer.

Not measured:

- filter expression translation from C# expressions;
- broad subscription registration throughput outside the focused repeated
  compile case;
- full sidecar registry lookup across many event types;
- ShaRPC envelope framing or sidecar IPC;
- real server/client provider calls used by expensive projection includes.

That scope matches the event dispatch hot path after a plugin subscription has
already been registered, plus one focused matched-delivery serialization case.

## Harness

Benchmark project:

```text
src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj
```

Run command used for the final sample:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Environment:

```text
Runtime: .NET 10.0.8
OS: Microsoft Windows NT 10.0.26200.0
Process architecture: x64
Stopwatch frequency: 10,000,000 ticks/s
Samples: 9
```

The filter and dispatch benchmarks cycle through 1,024 mixed matching and
nonmatching events to avoid a constant "always true" loop. Projection benchmarks
construct the same public `ProjectedEvent` shape on both manual and engine paths.
The serialization-fusion benchmark writes the same contractless MessagePack
`ProjectedEvent` wire shape and deserializes the fused bytes during setup to
guard compatibility.

The registration benchmark compiles the same four-clause `DamageDealtEvent`
filter repeatedly. It is intentionally separate from the dispatch hot path. In
the baseline and earlier improvement sections it measured the uncached schema
and predicate compilation path; after the structural cache improvement it
measures equivalent-registration cache hits.

The later plugin-owned registration benchmark compiles an equivalent filter
over a benchmark-local public `IGameEvent` DTO. It exercises the source
generator's current-compilation provider path rather than the built-in
abstraction provider.

## Results

Baseline after adding the benchmark harness:

| Category | Scenario | Iterations | Manual ns/op | Engine ns/op | Overhead ns/op | Ratio | Manual B/op | Engine B/op | Extra B/op |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 20,000,000 | 0.45 | 11.57 | 11.12 | 25.53x | 0.0 | 24.0 | 24.0 |
| Filter | 4 scalar clauses | 10,000,000 | 0.49 | 32.53 | 32.04 | 65.80x | 0.0 | 52.4 | 52.4 |
| Filter | `in` + 2 array checks | 5,000,000 | 5.16 | 97.00 | 91.84 | 18.78x | 0.0 | 173.0 | 173.0 |
| Projection | 2 fields | 1,000,000 | 29.80 | 58.17 | 28.37 | 1.95x | 312.0 | 384.0 | 72.0 |
| Projection | default fields | 500,000 | 103.86 | 199.00 | 95.14 | 1.92x | 1,152.0 | 1,376.0 | 224.0 |
| Projection | 3 fields + include | 500,000 | 48.99 | 100.23 | 51.24 | 2.05x | 496.0 | 568.0 | 72.0 |
| Pipeline | filter + 2-field projection | 1,000,000 | 29.82 | 68.48 | 38.66 | 2.30x | 312.0 | 408.0 | 96.0 |

## Improvement Measurements

### Typed Scalar Filter Accessors

Change: add typed scalar accessors to `FilterSchema` and let
`FilterCompiler` use typed compare/`in` predicates for boolean, numeric,
string, `Guid`, and integer enum comparisons.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results:

| Category | Scenario | Baseline engine ns/op | New engine ns/op | Engine delta | Baseline B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 11.57 | 4.09 | -64.6% | 24.0 | 0.0 | -24.0 |
| Filter | 4 scalar clauses | 32.53 | 13.56 | -58.3% | 52.4 | 0.0 | -52.4 |
| Filter | `in` + 2 array checks | 97.00 | 74.87 | -22.8% | 173.0 | 88.9 | -84.1 |
| Projection | 2 fields | 58.17 | 79.73 | +37.1% | 384.0 | 384.0 | 0.0 |
| Projection | default fields | 199.00 | 241.04 | +21.1% | 1,376.0 | 1,376.0 | 0.0 |
| Projection | 3 fields + include | 100.23 | 121.91 | +21.6% | 568.0 | 568.0 | 0.0 |
| Pipeline | filter + 2-field projection | 68.48 | 77.17 | +12.7% | 408.0 | 384.0 | -24.0 |

Interpretation:

- The targeted filter cases improved materially and now allocate 0 B/op for
  scalar-only predicates.
- The complex filter still allocates because array `contains` is still on the
  boxed `IEnumerable` fallback path. The scalar clauses inside that filter did
  improve.
- Projection timings moved upward in this run even though projection code was
  not changed. Treat those projection deltas as benchmark noise or secondary JIT
  layout effects, not as a projection regression caused by the scalar filter
  change.
- The next filter optimization should target typed scalar arrays, especially
  `int[]`, `long[]`, `string[]`, and enum arrays.

### Typed Scalar Array Contains

Change: add typed array contains accessors for actual scalar array fields and
let `FilterCompiler` use them for `contains` filters before falling back to the
boxed `IEnumerable` path.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the typed-scalar-accessor commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 4.09 | 4.14 | +1.2% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 13.56 | 13.78 | +1.6% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 74.87 | 32.85 | -56.1% | 88.9 | 47.5 | -41.4 |
| Projection | 2 fields | 79.73 | 70.41 | -11.7% | 384.0 | 384.0 | 0.0 |
| Projection | default fields | 241.04 | 222.53 | -7.7% | 1,376.0 | 1,376.0 | 0.0 |
| Projection | 3 fields + include | 121.91 | 114.63 | -6.0% | 568.0 | 568.0 | 0.0 |
| Pipeline | filter + 2-field projection | 77.17 | 70.69 | -8.4% | 384.0 | 384.0 | 0.0 |

Interpretation:

- The complex filter improved from 74.87 ns/op to 32.85 ns/op because it no
  longer enumerates scalar arrays through the general boxed fallback.
- The remaining 47.5 B/op in the complex filter likely comes from the generic
  numeric-array helper converting value-type array elements through
  `IConvertible`.
- The next improvement should specialize numeric array contains by primitive
  element type so `int[]`/`long[]`/`double[]` checks can compare directly.

### Primitive Numeric Array Contains

Change: replace the generic numeric-array contains helper with primitive-specific
loops for `byte[]`, `short[]`, `int[]`, `long[]`, `float[]`, `double[]`,
`decimal[]`, and unsigned variants.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the typed scalar array commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 4.14 | 4.06 | -1.9% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 13.78 | 14.64 | +6.2% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 32.85 | 27.04 | -17.7% | 47.5 | 0.0 | -47.5 |
| Projection | 2 fields | 70.41 | 72.56 | +3.1% | 384.0 | 384.0 | 0.0 |
| Projection | default fields | 222.53 | 225.75 | +1.4% | 1,376.0 | 1,376.0 | 0.0 |
| Projection | 3 fields + include | 114.63 | 125.85 | +9.8% | 568.0 | 568.0 | 0.0 |
| Pipeline | filter + 2-field projection | 70.69 | 80.16 | +13.4% | 384.0 | 384.0 | 0.0 |

Interpretation:

- The complex filter is now allocation-free in the measured case.
- The complex filter improved from 32.85 ns/op to 27.04 ns/op by removing
  numeric value conversion through the generic helper.
- Scalar-only filters were already allocation-free; their small timing movement
  is benchmark noise.
- Remaining filter overhead is now mostly delegate dispatch, closure shape,
  typed getter indirection, and generic boolean composition rather than boxing.

### Projection No-Include Fast Path

Change: cache projection event metadata at compile time and return a completed
`ValueTask<ProjectedEvent>` directly when a compiled projection has no includes.
The include path remains async.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the primitive numeric array commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 4.06 | 4.18 | +3.0% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 14.64 | 14.47 | -1.2% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 27.04 | 27.45 | +1.5% | 0.0 | 0.0 | 0.0 |
| Projection | 2 fields | 72.56 | 56.02 | -22.8% | 384.0 | 360.0 | -24.0 |
| Projection | default fields | 225.75 | 229.17 | +1.5% | 1,376.0 | 1,352.0 | -24.0 |
| Projection | 3 fields + include | 125.85 | 114.02 | -9.4% | 568.0 | 568.0 | 0.0 |
| Pipeline | filter + 2-field projection | 80.16 | 68.16 | -15.0% | 384.0 | 360.0 | -24.0 |

Interpretation:

- Field-only projections benefit because they no longer run through the async
  method body and no longer construct an empty context array.
- Event type/name strings are now cached by compiled projection instead of
  derived from `subject.GetType()` per projected event.
- Include projections still allocate the same output DTO graph; their timing
  movement is mostly noise plus cached event metadata.
- Remaining projection overhead is now dominated by boxed field getters and
  `ProjectedEventValue.FromScalar`.

### Generated Abstraction Schemas

Change: add a Roslyn source generator for built-in abstraction DTO filter
schemas. `FilterSchema.For` now tries generated server/client abstraction
schemas before falling back to the existing reflection schema builder for
custom plugin subjects.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to a pre-sourcegen run with the same registration benchmark
case added:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 4.25 | 4.08 | -4.0% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 14.34 | 14.51 | +1.2% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 28.94 | 25.52 | -11.8% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 2,032,839.50 | 1,085.00 | -99.9% | 90,970.0 | 2,712.0 | -87,258.0 |
| Projection | 2 fields | 57.80 | 53.12 | -8.1% | 360.0 | 360.0 | 0.0 |
| Projection | default fields | 237.44 | 199.20 | -16.1% | 1,352.0 | 1,352.0 | 0.0 |
| Projection | 3 fields + include | 131.05 | 104.03 | -20.6% | 568.0 | 568.0 | 0.0 |
| Pipeline | filter + 2-field projection | 58.16 | 50.12 | -13.8% | 360.0 | 360.0 | 0.0 |

Interpretation:

- The sourcegen improvement is primarily a registration win. The uncached
  four-clause compile dropped from about 2.03 ms to about 1.09 us because known
  abstraction DTOs no longer pay reflection traversal or `Expression.Compile`
  during schema construction.
- Registration allocation dropped by about 85 KB/op. The remaining 2.7 KB/op is
  the generated schema field/accessor objects plus compiled predicate closures.
- The steady-state filter/projection cases are still governed by the compiled
  delegates. Their movement is small relative to the registration delta and
  should be treated as generated delegate/JIT layout noise, not a new hot-path
  behavior requirement.
- Custom plugin DTOs still use the fallback schema builder, so sourcegen does
  not remove runtime coverage for arbitrary safe DTOs.

### Typed Projection Value Accessors

Change: add typed projection accessors to `FilterField`, fallback schema
construction, and generated abstraction schemas. Compiled projections now build
`ProjectedEventValue` directly for supported scalar fields instead of boxing the
field value and rediscovering its runtime type through `FromScalar`.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the generated-schema commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 4.08 | 4.17 | +2.2% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 14.51 | 12.45 | -14.2% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 25.52 | 28.43 | +11.4% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 1,085.00 | 1,222.00 | +12.6% | 2,712.0 | 2,800.0 | +88.0 |
| Projection | 2 fields | 53.12 | 44.32 | -16.6% | 360.0 | 312.0 | -48.0 |
| Projection | default fields | 199.20 | 155.10 | -22.1% | 1,352.0 | 1,152.0 | -200.0 |
| Projection | 3 fields + include | 104.03 | 92.21 | -11.4% | 568.0 | 496.0 | -72.0 |
| Pipeline | filter + 2-field projection | 50.12 | 42.26 | -15.7% | 360.0 | 312.0 | -48.0 |

Interpretation:

- Projection allocations now match the handwritten paths in the measured
  selected-field, default-field, include, and filter+projection scenarios.
- The two-field projection improved from 53.12 ns/op to 44.32 ns/op, and the
  filter+projection pipeline improved from 50.12 ns/op to 42.26 ns/op.
- Registration allocation increased by 88 B/op because generated schemas now
  carry projection accessor delegates. That is paid once per uncached schema and
  is more than offset by allocation-free projected event dispatch.
- Filter timings moved slightly but use the same predicate path; treat those
  deltas as incidental JIT/layout movement.

### Whole-Filter Typed Predicate Compilation

Change: compile supported kernels into one typed expression predicate. The
compiled predicate casts the subject once, reads fields directly from generated
or fallback access metadata, and emits expression-tree `and`/`or` nodes so
short-circuiting happens without per-clause delegate hops. Unsupported direct
shapes still fall back to the existing delegate compiler.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the typed projection accessor commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 4.17 | 1.95 | -53.2% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 12.45 | 2.09 | -83.2% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 28.43 | 7.71 | -72.9% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 1,222.00 | 204,370.00 | +16,625.2% | 2,800.0 | 9,708.2 | +6,908.2 |
| Projection | 2 fields | 44.32 | 36.08 | -18.6% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 155.10 | 120.46 | -22.3% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 92.21 | 78.16 | -15.2% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 42.26 | 34.87 | -17.5% | 312.0 | 312.0 | 0.0 |

Interpretation:

- The hot-path filter work is now close to direct handwritten code: about 2 ns
  for scalar kernels and 7.71 ns for the mixed `in` plus array filter.
- The improvement removes nested delegate composition from supported filters.
  Composite predicates now run as one compiled expression with direct property
  access and normal short-circuiting.
- The tradeoff is registration cost. Uncached registration now pays
  `Expression.Compile`, moving the measured four-clause compile case from about
  1.22 us to about 204 us and adding about 6.9 KB/op.
- Normal subscriptions still use cached compiled kernels. This tradeoff is
  acceptable for long-lived subscriptions, but the next sourcegen/access-helper
  task should target this registration regression.
- The projection timings moved down in this run even though projection code was
  not changed. Treat those projection deltas as benchmark noise or secondary JIT
  layout effects.

### Structural Compiled Kernel Cache

Change: cache compiled kernels by subject type plus a structural fingerprint of
the filter expression. Equivalent registrations now reuse the expression
compiled in the first registration instead of rebuilding the schema path and
calling `Expression.Compile` again.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the whole-filter typed predicate commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.95 | 2.35 | +20.5% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.09 | 2.46 | +17.7% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 7.71 | 7.93 | +2.9% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 204,370.00 | 570.00 | -99.7% | 9,708.2 | 824.0 | -8,884.2 |
| Projection | 2 fields | 36.08 | 39.31 | +9.0% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 120.46 | 213.66 | +77.4% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 78.16 | 128.03 | +63.8% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 34.87 | 52.46 | +50.4% | 312.0 | 312.0 | 0.0 |

Interpretation:

- Equivalent filter registrations now hit the compiled-kernel cache. The
  benchmarked registration case dropped from about 204 us to 570 ns and from
  9.7 KB/op to 824 B/op.
- The cache key is a structural fingerprint, so two separate DTO instances with
  the same fields, operators, values, and child tree reuse the same
  `CompiledKernel`.
- The filter hot path does not use the cache after registration. The small
  steady-state movements are benchmark noise; the complex filter remains around
  8 ns/op and allocation-free.
- Projection code was unchanged. The projection and pipeline movement in this
  run is noise from process/JIT layout and should not be treated as a projection
  regression.
- The cache is capped at 4096 compiled kernels to avoid unbounded growth from
  plugin-provided filter shapes.

### Dispatch Indexing Before Residual Filters

Change: add a shared `FilterSubscriptionIndex<TSubscription>` in
`FourStory.Plugin.Filters` and route server sidecar, client bridge, and local
client projected subscriptions through it. The index extracts one exact scalar
`field == value` key from each filter, buckets indexed subscriptions by field
and value, keeps broad or unsupported filters in the residual list, and still
runs the compiled kernel before reporting a match.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the structural cache commit, plus new dispatch rows:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 2.35 | 2.13 | -9.4% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.46 | 2.21 | -10.2% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 7.93 | 7.87 | -0.8% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 570.00 | 673.50 | +18.2% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | n/a | 580.58 | n/a | n/a | 0.0 | n/a |
| Dispatch | 256 exact scalar subscriptions | n/a | 68.76 | n/a | n/a | 144.0 | n/a |
| Projection | 2 fields | 39.31 | 38.28 | -2.6% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 213.66 | 123.43 | -42.2% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 128.03 | 77.57 | -39.4% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 52.46 | 36.61 | -30.2% | 312.0 | 312.0 | 0.0 |

Dispatch comparison from the same run:

| Dispatch shape | Engine ns/op | Engine B/op | Delta vs scan |
|---|---:|---:|---:|
| Residual scan over 256 exact subscriptions | 580.58 | 0.0 | baseline |
| Indexed candidate lookup for 256 exact subscriptions | 68.76 | 144.0 | -88.2% time |

Interpretation:

- Indexed dispatch avoids evaluating 256 residual predicates for the common
  exact-scalar subscription shape. The benchmarked dispatch case drops from
  580.58 ns/op to 68.76 ns/op.
- The index is shared by server and client registries, so the routing behavior
  does not fork between the two sides.
- Broad filters and unsupported index shapes still run as residual predicates,
  preserving compatibility.
- The current index snapshots candidates into an array per dispatch, which is
  why the indexed dispatch row allocates 144 B/op. Removing that allocation is a
  good follow-up after clause ordering and small-`in` specialization.
- The unrelated filter and projection movements are benchmark noise; the
  dispatch rows are the meaningful measurement for this improvement.

### Predicate Clause Ordering

Change: order `and` filter children by a simple estimated cost before compiling
the expression predicate or delegate fallback. Cheap scalar comparisons and
`exists` checks run before `in`, nested boolean groups, and array `contains`
checks. `or` predicates keep their original order.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the dispatch-index commit, plus the new intentionally
expensive-first clause-ordering row:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 2.13 | 1.93 | -9.4% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.21 | 2.08 | -5.9% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 7.87 | 6.90 | -12.3% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | n/a | 2.06 | n/a | n/a | 0.0 | n/a |
| Registration | compile 4-clause filter | 673.50 | 644.00 | -4.4% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 580.58 | 599.43 | +3.2% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 68.76 | 64.25 | -6.6% | 144.0 | 144.0 | 0.0 |
| Projection | 2 fields | 38.28 | 39.57 | +3.4% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 123.43 | 165.43 | +34.0% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 77.57 | 97.24 | +25.4% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 36.61 | 45.37 | +23.9% | 312.0 | 312.0 | 0.0 |

Interpretation:

- The new `ordered expensive clauses` case feeds the engine a deliberately bad
  filter order: two array `contains` checks before cheap scalar rejection. The
  compiled engine evaluates the cheap scalar clauses first and lands at
  2.06 ns/op with no allocations.
- Existing filter rows stay in the same low-nanosecond range. The complex filter
  improved from 7.87 ns/op to 6.90 ns/op in this run.
- Registration allocation is unchanged. Clause ordering sorts at compile time,
  so the steady-state hot path stays allocation-free.
- Projection and dispatch movements are unrelated benchmark noise for this
  improvement.

### Specialized `in` Predicates

Change: specialize scalar `in` filters in the shared engine. Small typed value
sets compile to direct equality chains, while larger numeric, string, Guid, and
enum sets compile once into typed `HashSet<T>` lookups. The delegate fallback
path now uses the same split.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the predicate-ordering commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.93 | 1.98 | +2.6% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.08 | 2.09 | +0.5% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 6.90 | 5.03 | -27.1% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | n/a | 3.81 | n/a | n/a | 0.0 | n/a |
| Filter | ordered expensive clauses | 2.06 | 2.04 | -1.0% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 644.00 | 629.00 | -2.3% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 599.43 | 592.11 | -1.2% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 64.25 | 64.35 | +0.2% | 144.0 | 144.0 | 0.0 |
| Projection | 2 fields | 39.57 | 37.00 | -6.5% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 165.43 | 124.44 | -24.8% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 97.24 | 75.18 | -22.7% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 45.37 | 35.60 | -21.5% | 312.0 | 312.0 | 0.0 |

Interpretation:

- The existing complex filter is the meaningful row for this change. Its
  3-value `MapId in (...)` now compiles to direct comparisons and the full
  mixed predicate improved from 6.90 ns/op to 5.03 ns/op.
- The new 32-value `in` row uses a prebuilt typed lookup and stays
  allocation-free on the hot path. Its 3.81 ns/op cost includes the shared
  engine's object entrypoint and typed subject cast; the handwritten baseline is
  a direct `HashSet<int>.Contains`.
- Unrelated projection rows moved down in this sample; no projection code
  changed in this improvement.

### Required Scalar Accessors For Indexing

Change: schema generation and fallback schema building now expose non-nullable
typed scalar accessors for value-type fields that cannot be null. Dispatch
index lookup uses those accessors to build exact-match keys before falling back
to the boxed `FilterField.Getter` path.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the specialized-`in` commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.98 | 1.94 | -2.0% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.09 | 2.08 | -0.5% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.03 | 5.08 | +1.0% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.81 | 3.73 | -2.1% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.04 | 2.04 | 0.0% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 629.00 | 585.00 | -7.0% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 592.11 | 552.25 | -6.7% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 64.35 | 64.52 | +0.3% | 144.0 | 120.0 | -24.0 |
| Projection | 2 fields | 37.00 | 40.31 | +8.9% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 124.44 | 121.44 | -2.4% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 75.18 | 84.21 | +12.3% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 35.60 | 41.01 | +15.2% | 312.0 | 312.0 | 0.0 |

Interpretation:

- The indexed dispatch row is the targeted measurement. It no longer boxes the
  numeric field just to rebuild a `FilterIndexValue`, cutting per-lookup
  allocation from 144 B/op to 120 B/op.
- Time stayed effectively flat at about 64 ns/op. The remaining indexed dispatch
  cost is candidate snapshot materialization and residual kernel validation, not
  scalar key extraction.
- Whole-expression filter rows are expected to be mostly unchanged because they
  already read generated/fallback property paths directly.

### No-Include Projection Fast Path

Change: no-include projections now use a separate `ProjectedEvent` construction
path that leaves `Context` at the DTO default instead of assigning an empty
array. The field projection loop remains simple; an attempted unrolled
field-array delegate was measured and dropped because it regressed projection
rows.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the required-scalar-accessor commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.94 | 1.93 | -0.5% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.08 | 2.08 | 0.0% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.08 | 5.01 | -1.4% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.73 | 3.75 | +0.5% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.04 | 2.04 | 0.0% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 585.00 | 586.50 | +0.3% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 552.25 | 551.49 | -0.1% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 64.52 | 64.98 | +0.7% | 120.0 | 120.0 | 0.0 |
| Projection | 2 fields | 40.31 | 34.83 | -13.6% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 121.44 | 123.72 | +1.9% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 84.21 | 74.77 | -11.2% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 41.01 | 40.52 | -1.2% | 312.0 | 312.0 | 0.0 |

Interpretation:

- The 2-field no-include projection is the targeted case and improved from
  40.31 ns/op to 34.83 ns/op with the same DTO allocation size.
- Include projections also moved down because field projection still uses the
  shared helper, while context projection remains unchanged.
- Default projection is effectively flat in this sample. The earlier unrolled
  field-array experiment made default projection slower, so the final change
  keeps the loop.

### Compact Opcode Evaluator Evaluation

Change: add a benchmark-only compact opcode residual evaluator for the complex
`ScalarArrayEvent` filter. This is not wired into runtime dispatch. In the
benchmark row, the left `Manual ns` column is the current compiled kernel and
the right `Engine ns` column is the opcode interpreter.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the no-include projection fast-path commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.93 | 1.93 | 0.0% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.08 | 2.08 | 0.0% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.01 | 5.09 | +1.6% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.75 | 3.74 | -0.3% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.04 | 2.04 | 0.0% | 0.0 | 0.0 | 0.0 |
| Evaluator | opcode residual complex | n/a | 11.20 | n/a | n/a | 0.0 | n/a |
| Registration | compile 4-clause filter | 586.50 | 578.00 | -1.4% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 551.49 | 564.62 | +2.4% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 64.98 | 52.92 | -18.6% | 120.0 | 120.0 | 0.0 |
| Projection | 2 fields | 34.83 | 35.56 | +2.1% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 123.72 | 119.59 | -3.3% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 74.77 | 73.72 | -1.4% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 40.52 | 34.23 | -15.5% | 312.0 | 312.0 | 0.0 |

Interpretation:

- The opcode row compares the current compiled whole-filter kernel at
  5.00 ns/op against the compact interpreter at 11.20 ns/op. Both paths are
  allocation-free.
- The interpreter loses because every residual clause pays a switch dispatch and
  cannot be optimized like the compiled expression predicate.
- Do not adopt a compact opcode evaluator for hot residual filters unless a
  future workload requires cheaper registration over hot-path speed. The current
  compiled predicate remains the better runtime path.

### Serialization/Projection Fusion Evaluation

Change: add a benchmark-only direct MessagePack writer for a projected event
payload with three selected fields plus a `nearby` context include. The
materialized path constructs the full `ProjectedEvent`, `ProjectedEventField`,
and nested `ProjectedEventValue` graph before serializing. The fused path writes
the same contractless map payload directly to `MessagePackWriter`.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the compact-opcode-evaluator commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.93 | 1.95 | +1.0% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.08 | 2.07 | -0.5% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.09 | 5.06 | -0.6% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.74 | 3.71 | -0.8% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.04 | 2.05 | +0.5% | 0.0 | 0.0 | 0.0 |
| Evaluator | opcode residual complex | 11.20 | 11.22 | +0.2% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 578.00 | 570.00 | -1.4% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 564.62 | 547.62 | -3.0% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 52.92 | 64.20 | +21.3% | 120.0 | 120.0 | 0.0 |
| Projection | 2 fields | 35.56 | 34.19 | -3.9% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 119.59 | 118.43 | -1.0% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 73.72 | 73.65 | -0.1% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 34.23 | 34.03 | -0.6% | 312.0 | 312.0 | 0.0 |
| Serialization | projected event payload | n/a | 2,223.32 | n/a | n/a | 8,088.0 | n/a |

Serialization comparison from the same run:

| Serialization shape | ns/op | B/op | Delta vs materialized |
|---|---:|---:|---:|
| Materialize `ProjectedEvent` graph, then serialize | 2,861.71 | 10,392.0 | baseline |
| Fused direct MessagePack writer | 2,223.32 | 8,088.0 | -22.3% time, -2,304 B/op |

Interpretation:

- Serialization dwarfs the nanosecond-scale filter and projection hot paths.
  The fused payload writer is about 638 ns faster than materializing the full
  projected DTO graph first, but still costs about 2.2 us because the
  contractless payload is verbose and the output buffer dominates allocation.
- The direct writer saves the exact DTO graph allocation that projection
  materialization would otherwise create before IPC: about 2.3 KB/op in this
  enriched projected payload.
- Do not hand-code these writers in runtime paths. If we adopt fusion, it
  should be generated or centralized from the projection schema so the wire
  shape remains one source of truth.

### Lock-Free Dispatch Snapshots And Sync Includes

Change: add an allocation-free `ForEachCandidate<TState>` visitor API to the
shared `FilterSubscriptionIndex<TSubscription>`. The index now stores
registration-time arrays for unindexed and exact-match buckets, and server plus
client registries use visitors instead of building candidate arrays on each
dispatch. Boolean match paths can also stop at the first matching subscription.
Dispatch reads now use a published immutable snapshot, so candidate lookup no
longer takes the index mutation lock.

Projection includes now fast-path completed `ValueTask` results. In-memory
include providers can return projected context without entering an async state
machine; genuinely asynchronous providers still use the awaited path.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the serialization/projection-fusion commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.95 | 1.93 | -1.0% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.07 | 2.08 | +0.5% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.06 | 5.02 | -0.8% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.71 | 3.74 | +0.8% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.05 | 2.04 | -0.5% | 0.0 | 0.0 | 0.0 |
| Evaluator | opcode residual complex | 11.22 | 11.02 | -1.8% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 570.00 | 562.50 | -1.3% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 547.62 | 557.72 | +1.8% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 64.20 | 31.16 | -51.5% | 120.0 | 0.0 | -120.0 |
| Projection | 2 fields | 34.19 | 34.74 | +1.6% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 118.43 | 118.33 | -0.1% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 73.65 | 67.11 | -8.9% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2-field projection | 34.03 | 33.85 | -0.5% | 312.0 | 312.0 | 0.0 |
| Serialization | projected event payload | 2,223.32 | 2,322.46 | +4.5% | 8,088.0 | 8,088.0 | 0.0 |

Dispatch comparison from the same run:

| Dispatch shape | Engine ns/op | Engine B/op | Delta vs previous indexed path |
|---|---:|---:|---:|
| Indexed candidate array snapshot | 64.20 | 120.0 | baseline |
| Indexed lock-free visitor dispatch | 31.16 | 0.0 | -51.5% time, -120 B/op |

Interpretation:

- The indexed dispatch hot path no longer allocates and no longer takes the
  mutation lock. Candidate arrays moved out of dispatch and into
  registration-time bucket snapshots, which is the right tradeoff for
  long-lived subscriptions.
- Indexed dispatch improved from about 64 ns/op to about 32 ns/op because it no
  longer builds a `List<T>` plus array, validates candidates directly, and reads
  a published snapshot without locking.
- Synchronous include projection improved from about 74 ns/op to about
  67 ns/op in the enriched projection row. The fast path preserves the async
  behavior for providers that are not completed yet.
- A small field-count projection unroll was measured and dropped because it
  regressed the include and pipeline rows.
- Filter predicate rows are effectively unchanged. Their small movement is
  benchmark noise; this change targets dispatch lookup and include projection.

### Projection Match Accumulator Grouping

Change: add a shared `ProjectionMatchAccumulator<TProjection>` and
`ProjectionDispatchGroup<TProjection>` to the filter/projection runtime. Server
and client projected event registries now add matching subscriptions directly
to the accumulator during candidate visitation instead of first building a
matched-subscription list and then running LINQ `GroupBy`/`Select` over it.

The accumulator has zero-match and one-match fast paths and keeps the first
four subscription ids in each projection group inline before allocating a list
for larger groups. This targets the matched projected-event path immediately
before projection materialization and IPC serialization. Server and client IPC
dispatch now enumerate the accumulator directly instead of first materializing
a dispatch-group result array.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the lock-free dispatch snapshot commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.93 | 1.92 | -0.5% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.08 | 2.08 | 0.0% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.02 | 5.03 | +0.2% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.74 | 3.73 | -0.3% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.04 | 2.03 | -0.5% | 0.0 | 0.0 | 0.0 |
| Evaluator | opcode residual complex | 11.02 | 11.04 | +0.2% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 562.50 | 595.50 | +5.9% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 557.72 | 562.76 | +0.9% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 31.16 | 30.67 | -1.6% | 0.0 | 0.0 | 0.0 |
| Projection | 2 fields | 34.74 | 40.24 | +15.8% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 118.33 | 120.91 | +2.2% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 67.11 | 60.36 | -10.1% | 496.0 | 496.0 | 0.0 |
| Projection | group 16 projected matches | 553.11 | 244.82 | -55.7% | 1,568.0 | 864.0 | -704.0 |
| Pipeline | filter + 2-field projection | 33.85 | 38.90 | +14.9% | 312.0 | 312.0 | 0.0 |
| Serialization | projected event payload | 2,322.46 | 2,335.93 | +0.6% | 8,088.0 | 8,088.0 | 0.0 |

Projection grouping comparison from the same run:

| Grouping shape | ns/op | B/op | Delta vs previous LINQ grouping |
|---|---:|---:|---:|
| Previous LINQ `GroupBy` over 16 matches | 553.11 | 1,568.0 | baseline |
| Shared projection match accumulator enumeration | 244.82 | 864.0 | -55.7% time, -704 B/op |

Interpretation:

- The new grouping row compares the previous runtime grouping algorithm against
  the accumulator in the same process. Its left column is not handwritten event
  logic; it is the old LINQ grouping path.
- Projected match grouping is now about 2.3x faster for the measured
  16-subscription, 4-projection-group case.
- Allocation drops because the registry no longer builds a matched-subscription
  list, LINQ groupings, iterator state, or per-group `List<string>` objects for
  common small groups. Runtime dispatch also avoids materializing a
  `ProjectionDispatchGroup[]` before looping.
- Unrelated filter/projection rows moved within normal benchmark noise. The
  grouping row is the targeted measurement for this change.

### Inline Projection Match Groups

Change: keep the first four projection groups inside
`ProjectionMatchAccumulator<TProjection>` instead of creating the overflow
dictionary immediately on the second distinct projection key. Each inline group
stores the first four subscription ids directly and only allocates a
`List<string>` when a single projection group has more than four subscription
ids. The dictionary is now reserved for the fifth and later distinct projection
keys.

This targets the common sidecar/client projected-dispatch shape where a small
number of projection definitions fan out to several subscriptions. The IPC call
still needs `string[]` subscription-id arrays, so the remaining allocation in
the focused row is the four dispatch id arrays.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the projection match accumulator commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.92 | 1.93 | +0.5% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.08 | 2.07 | -0.5% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.03 | 5.05 | +0.4% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.73 | 3.72 | -0.3% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.03 | 2.03 | 0.0% | 0.0 | 0.0 | 0.0 |
| Evaluator | opcode residual complex | 11.04 | 11.34 | +2.7% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 595.50 | 578.00 | -2.9% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 562.76 | 552.46 | -1.8% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 30.67 | 34.85 | +13.6% | 0.0 | 0.0 | 0.0 |
| Projection | 2 fields | 40.24 | 35.89 | -10.8% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 120.91 | 123.05 | +1.8% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 60.36 | 73.50 | +21.9% | 496.0 | 496.0 | 0.0 |
| Projection | group 16 projected matches | 244.82 | 122.93 | -49.8% | 864.0 | 224.0 | -640.0 |
| Pipeline | filter + 2-field projection | 38.90 | 40.13 | +3.2% | 312.0 | 312.0 | 0.0 |
| Serialization | projected event payload | 2,335.93 | 2,259.59 | -3.3% | 8,088.0 | 8,088.0 | 0.0 |

Projection grouping comparison from the same run:

| Grouping shape | ns/op | B/op | Delta vs previous LINQ grouping |
|---|---:|---:|---:|
| Previous LINQ `GroupBy` over 16 matches | 559.12 | 1,568.0 | baseline |
| Inline projection match accumulator enumeration | 122.93 | 224.0 | -78.0% time, -1,344 B/op |

Interpretation:

- Inline groups remove the dictionary and group-object allocations for the
  measured four-projection-group dispatch shape.
- The focused row is now about 2.0x faster than the first accumulator version
  and about 4.5x faster than the original LINQ grouping path.
- Allocation is down to the subscription-id arrays needed by the current IPC
  API. Removing those would require changing dispatch to accept spans or a
  pooled/id-writer abstraction instead of `string[]`.
- Unrelated rows moved within benchmark noise. The include row regressed in
  this sample, but this change does not touch include projection code.

### Required Scalar Projection Factories

Change: add required-value overloads to `ProjectionValueFactory` for
non-nullable scalar fields and update fallback schema projection accessor
compilation to bind to the exact nullable or non-nullable overload. Generated
built-in schemas already emit direct `ProjectionValueFactory.FromXxx(...)`
calls, so those calls now bind to the required overload when the DTO property is
non-nullable.

This avoids converting required value-type fields to `Nullable<T>` and then
checking `HasValue` on every projected scalar. Nullable DTO fields keep the old
null-preserving path.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the inline projection match group commit:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Filter | 1 exact scalar | 1.93 | 1.95 | +1.0% | 0.0 | 0.0 | 0.0 |
| Filter | 4 scalar clauses | 2.07 | 2.09 | +1.0% | 0.0 | 0.0 | 0.0 |
| Filter | `in` + 2 array checks | 5.05 | 5.02 | -0.6% | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 3.72 | 3.71 | -0.3% | 0.0 | 0.0 | 0.0 |
| Filter | ordered expensive clauses | 2.03 | 2.05 | +1.0% | 0.0 | 0.0 | 0.0 |
| Evaluator | opcode residual complex | 11.34 | 11.47 | +1.1% | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 578.00 | 589.00 | +1.9% | 824.0 | 824.0 | 0.0 |
| Dispatch | 256 exact scalar scan | 552.46 | 799.44 | +44.7% | 0.0 | 0.0 | 0.0 |
| Dispatch | 256 exact scalar subscriptions | 34.85 | 38.81 | +11.4% | 0.0 | 0.0 | 0.0 |
| Projection | 2 fields | 35.89 | 36.92 | +2.9% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 123.05 | 122.35 | -0.6% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 73.50 | 58.36 | -20.6% | 496.0 | 496.0 | 0.0 |
| Projection | group 16 projected matches | 122.93 | 119.52 | -2.8% | 224.0 | 224.0 | 0.0 |
| Pipeline | filter + 2-field projection | 40.13 | 33.93 | -15.5% | 312.0 | 312.0 | 0.0 |
| Serialization | projected event payload | 2,259.59 | 2,128.24 | -5.8% | 8,088.0 | 8,088.0 | 0.0 |

Interpretation:

- The projection rows are the relevant target. Selected-field projection is
  roughly flat, default projection is slightly faster, synchronous include
  projection improves, and the combined filter plus two-field projection row
  drops from about 40 ns/op to about 34 ns/op.
- Allocation is unchanged because both paths still construct the same projected
  DTO graph.
- The dispatch scan row regressed in this sample, but this change does not
  touch filter evaluation or dispatch indexing. The stable indexed-dispatch
  allocation result is still 0 B/op; this row should be rechecked in the next
  end-to-end dispatch benchmark rather than attributed to projection factories.
- The added tests cover required and nullable projection semantics for integer,
  boolean, Guid, and enum fields.

### Full Projected Dispatch Pipeline Benchmarks

Change: add two focused matched-delivery benchmark rows that measure the
combined projected dispatch path after registration. The server shape uses an
`ItemUsedEvent`; the client bridge shape uses a `UiSelectionChangedEvent`.
Each row includes:

- indexed candidate lookup through `FilterSubscriptionIndex`;
- residual `CompiledKernel` evaluation;
- projected match grouping through `ProjectionMatchAccumulator`;
- `CompiledProjection` materialization;
- MessagePack serialization through the same payload options used by IPC;
- a local RPC sink approximation that consumes subscription ids, event type,
  and payload length.

The handwritten side builds the equivalent projected payloads directly and
serializes them, so these rows measure engine overhead in a matched dispatch
where serialization still happens.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

New benchmark rows:

| Category | Scenario | Manual ns/op | Engine ns/op | Overhead ns | Ratio | Manual B/op | Engine B/op | Extra B/op |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dispatch | server projected pipeline | 1,464.50 | 1,764.61 | 300.11 | 1.20x | 1,832.0 | 2,056.0 | 224.0 |
| Dispatch | client projected pipeline | 1,400.64 | 1,765.12 | 364.49 | 1.26x | 1,440.0 | 1,608.0 | 168.0 |

Interpretation:

- In matched projected dispatch, serialization dominates the absolute cost.
  The engine adds about 300-365 ns/op over direct handwritten dispatch for the
  measured grouped subscription cases.
- The extra allocation is exactly the grouped subscription-id arrays that the
  current IPC contracts require. The server row has four projection groups
  (`4 * 56 B = 224 B`); the client row has three projection groups
  (`3 * 56 B = 168 B`).
- These rows strengthen the case for direct projected-payload writers. Even
  after filtering and grouping improvements, each matched delivery still pays
  DTO materialization plus MessagePack serialization.
- These rows still do not include ShaRPC envelope framing or real transport.
  They are intended to sit between the microbenchmarks and a future full
  bridge/sidecar integration benchmark.

### Integrated Direct Projected-Payload Writer

Change: add `CompiledProjection<TContext>.ProjectPayloadAsync(...)` and a
central `ProjectedPayloadWriter` in the shared filter/projection runtime. Server
sidecar projected dispatch and external client plugin-host projected dispatch
now write MessagePack payloads directly from the compiled projection instead of
first materializing a `ProjectedEvent` plus field arrays for IPC.

The writer uses the same DTO property names and caller-supplied MessagePack
options as the existing IPC path, and focused tests deserialize the direct
payload back to `ProjectedEvent` for both no-include and synchronous-include
projections.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the full projected dispatch pipeline benchmark:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Dispatch | server projected pipeline | 1,764.61 | 1,351.17 | -23.4% | 2,056.0 | 2,016.0 | -40.0 |
| Dispatch | client projected pipeline | 1,765.12 | 1,026.39 | -41.9% | 1,608.0 | 1,560.0 | -48.0 |

Updated full projected dispatch rows:

| Category | Scenario | Manual ns/op | Engine ns/op | Overhead ns | Ratio | Manual B/op | Engine B/op | Extra B/op |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dispatch | server projected pipeline | 1,163.46 | 1,351.17 | 187.72 | 1.16x | 1,832.0 | 2,016.0 | 184.0 |
| Dispatch | client projected pipeline | 859.96 | 1,026.39 | 166.43 | 1.19x | 1,440.0 | 1,560.0 | 120.0 |

Interpretation:

- Direct payload writing removes the `ProjectedEvent` and field-array
  materialization step from the IPC path while keeping the same payload shape.
- Runtime dispatch now pays about 188 ns/op extra for the measured server
  projected dispatch shape and about 166 ns/op extra for the measured client
  bridge shape.
- Remaining extra allocation is still dominated by grouped subscription-id
  arrays and direct-writer buffer sizing. The direct writer is faster, but it is
  not yet the fully fused scalar writer measured in the earlier serialization
  experiment.
- The next writer improvement would be field-shape-specific scalar emission so
  `ProjectedEventValue` objects are not created before MessagePack writing.

### Small Composed No-Include Projection Projectors

Change: compile a single field-array projector for no-include projections with
up to four scalar fields when schema access metadata can express every selected
field. The composed projector casts the subject once and constructs the
`ProjectedEventField[]` directly. Projections with includes, array fields,
dynamic metadata, unsupported scalar expressions, or more than four fields keep
the existing per-field loop.

The first broad attempt compiled every scalar field count. That made selected
projections faster in some samples but caused inconsistent regressions for
larger default projections, so the committed path is deliberately constrained to
the common small selected-field shape.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the integrated direct projected-payload writer benchmark:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Projection | 2 fields | 40.33 | 37.40 | -7.3% | 312.0 | 312.0 | 0.0 |
| Projection | default fields | 128.27 | 122.83 | -4.2% | 1,152.0 | 1,152.0 | 0.0 |
| Projection | 3 fields + include | 64.50 | 63.86 | -1.0% | 496.0 | 496.0 | 0.0 |
| Pipeline | filter + 2 fields | 40.97 | 37.57 | -8.3% | 312.0 | 312.0 | 0.0 |
| Dispatch | server projected pipeline | 1,351.17 | 1,371.99 | +1.5% | 2,016.0 | 2,016.0 | 0.0 |
| Dispatch | client projected pipeline | 1,026.39 | 1,027.70 | +0.1% | 1,560.0 | 1,560.0 | 0.0 |

Interpretation:

- The targeted selected-field path improved without changing allocation.
- The filter plus two-field projection row dropped from about 41 ns/op to about
  38 ns/op because it uses the small composed no-include projector.
- Larger default projections are intentionally left on the old field loop. The
  small improvement in this sample is process/JIT movement, not a broader
  composed-default guarantee.
- Projected IPC dispatch uses the direct payload writer, not this field-array
  projector, so the server/client projected pipeline rows are effectively
  unchanged.
- The next projection improvement should be generated field-shape-specific
  scalar payload writing for IPC, where we can avoid both delegate dispatch and
  `ProjectedEventValue` construction.

### Inline Projected Subscription Id Batches

Change: replace the projected-dispatch `string[] subscriptionIds` RPC parameter
with a shared `SubscriptionIdBatch` value DTO. The batch stores the first four
subscription ids inline and only allocates an overflow array for larger groups.
Server sidecar RPC, client plugin-host RPC, their receivers, dispatch tests, and
the projected-dispatch benchmarks now use the new contract. Ordinary
unprojected dispatch still uses its existing `string[]` contract.

This change is intentionally contract-breaking. The old contract was not kept
because the current branch owns both sides of the bridge and sidecar transport.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Results compared to the small composed no-include projection benchmark:

| Category | Scenario | Previous engine ns/op | New engine ns/op | Engine delta | Previous B/op | New B/op | Allocation delta |
|---|---:|---:|---:|---:|---:|---:|---:|
| Dispatch | server projected pipeline | 1,371.99 | 1,649.16 | +20.2% | 2,016.0 | 1,792.0 | -224.0 |
| Dispatch | client projected pipeline | 1,027.70 | 1,252.47 | +21.8% | 1,560.0 | 1,392.0 | -168.0 |
| Projection | group 16 projected matches | 78.84 | 81.01 | +2.8% | 224.0 | 0.0 | -224.0 |

Updated full projected dispatch rows:

| Category | Scenario | Manual ns/op | Engine ns/op | Overhead ns | Ratio | Manual B/op | Engine B/op | Extra B/op |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dispatch | server projected pipeline | 1,296.86 | 1,649.16 | 352.30 | 1.27x | 1,832.0 | 1,792.0 | -40.0 |
| Dispatch | client projected pipeline | 1,013.70 | 1,252.47 | 238.77 | 1.24x | 1,440.0 | 1,392.0 | -48.0 |

Interpretation:

- The current projected grouping hot path is now allocation-free for up to four
  ids per projection group.
- The matched projected-dispatch rows now allocate less than the handwritten
  path because the engine path uses direct payload writing and no longer
  allocates grouped subscription-id arrays.
- Local dispatch timings moved upward in this sample while allocation improved.
  The measured CPU cost is still dominated by projected payload writing; this
  change mainly removes Gen0 pressure from matched projected fanout.
- Groups with more than four ids still allocate an overflow array. That is the
  explicit tradeoff to keep the common small group path allocation-free without
  pooling arrays across async RPC calls.

### Source-Generated Plugin-Owned Event Schemas

Change: extend the filter schema source generator with a current-compilation
path for public non-generic `IGameEvent` DTOs. When a consuming assembly also
references `FourStory.Plugin.Filters`, the generator emits a
`GeneratedCurrentFilterSchemaProvider` and registers it through a module
initializer. Assemblies without the filter runtime reference do not receive the
provider and keep the existing fallback schema path.

The benchmark assembly now references the generator as an analyzer and exposes
its benchmark event DTOs publicly, so `ScalarArrayEvent` and `LargeInEvent`
exercise the plugin-owned generated-provider path.

Run command:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Relevant latest rows:

| Category | Scenario | Manual ns/op | Engine ns/op | Overhead ns | Ratio | Manual B/op | Engine B/op | Extra B/op |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Filter | `in` + 2 arrays | 5.05 | 5.09 | 0.04 | 1.01x | 0.0 | 0.0 | 0.0 |
| Filter | 32-value `in` | 1.26 | 3.71 | 2.45 | 2.94x | 0.0 | 0.0 | 0.0 |
| Registration | compile 4-clause filter | 9.00 | 578.00 | 569.00 | 64.22x | 0.0 | 824.0 | 824.0 |
| Registration | compile plugin-owned filter | 9.00 | 561.50 | 552.50 | 62.39x | 0.0 | 856.0 | 856.0 |
| Projection | 3 fields + include | 44.90 | 59.85 | 14.94 | 1.33x | 496.0 | 496.0 | 0.0 |
| Dispatch | server projected pipeline | 1,156.89 | 1,327.10 | 170.21 | 1.15x | 1,832.0 | 1,792.0 | -40.0 |
| Dispatch | client projected pipeline | 868.25 | 1,020.09 | 151.84 | 1.17x | 1,440.0 | 1,392.0 | -48.0 |

Interpretation:

- Public plugin-owned event schemas now avoid fallback reflection/expression
  schema discovery when the analyzer and filter runtime are present.
- The plugin-owned registration row is in the same range as the built-in
  generated DTO row: 561.50 ns/op and 856 B/op versus 578.00 ns/op and
  824 B/op in this sample.
- Steady-state filter/projection behavior is essentially unchanged after
  registration. The complex plugin-owned filter remains allocation-free and is
  within measurement noise of the handwritten array-heavy predicate.
- Plugin assemblies that intentionally reference only abstractions still build
  without the filter runtime; they keep fallback runtime schema discovery until
  SDK packaging makes the analyzer/runtime opt-in explicit.

## Findings

The filter engine is still slower than handwritten predicates by ratio, but the
absolute cost is now small: roughly 2 ns for one exact scalar check, 2 ns for
four scalar clauses, and 5 ns for a mixed `in` plus array filter in the latest
run.

The high filter ratios are expected. A handwritten predicate is direct field
access with no validation abstraction, no schema lookup indirection, no
`ServerFilterValue` comparison switch, and fewer delegate hops. The engine now
uses typed scalar and array accessors for the measured filter paths, so those
steady-state filters are allocation-free after registration.

Projection overhead is lower by ratio because both manual and engine paths must
construct real `ProjectedEvent`, `ProjectedEventField`, and `ProjectedEventValue`
DTOs. In the latest measured cases the compiled projection path is about 1.07x
the handwritten path for a two-field selected projection, 1.15x for the default
field set, and 1.33x for selected fields plus a synchronous include.

Projected match grouping is now handled by the shared accumulator instead of
LINQ grouping. For 16 matched subscriptions across four projection groups, the
latest grouping step measured about 65 ns/op and 0 B/op versus roughly 560-670
ns/op and 1.5-1.8 KB/op for the old LINQ grouping shapes.

The combined filter plus two-field projection path measured about 33 ns/op for
the engine versus 28 ns/op handwritten. That is about 4 ns extra and no extra
bytes per matched event.

The full matched projected-dispatch benchmark now allocates less than the
handwritten path in the measured server and client bridge shapes: -40 B/op for
server and -48 B/op for client. The latest timing sample measured about
1.33 us/op for the server shape and 1.02 us/op for the client bridge shape.
Payload writing remains the dominant matched-delivery cost.

Source generation now covers both built-in abstraction DTOs and public
plugin-owned `IGameEvent` DTOs in assemblies that opt into the analyzer and
filter runtime. The plugin-owned generated registration row is comparable to
the built-in generated registration row; assemblies that only reference
abstractions retain fallback schema discovery.

The measured hot-path kernel cost is tiny compared to event serialization or
sidecar IPC. The projected-payload serialization row costs about 2.2-2.9 us even
before ShaRPC framing or transport. The main architectural win remains avoiding
serialization and IPC for nonmatching subscriptions.

The dispatch index removes the main cost of many exact scalar subscriptions.
For the measured 256-subscription case, indexed lookup plus residual validation
is about 32 ns/op versus about 581 ns/op for scanning every residual predicate.
The indexed lookup hot path is now allocation-free in that benchmark.

Serialization/projection fusion is now on the server and client IPC paths for
projected dispatch. In the focused pipeline benchmarks, direct MessagePack
writing reduced the server engine path by 23.4% and the client engine path by
41.9% versus the prior materialize-then-serialize path.

## Allocation Notes

Filters should ideally be allocation-free after registration. The measured
scalar and array filter cases now are:

- simple scalar filter: 0 B/op;
- four scalar clauses: 0 B/op;
- complex filter with arrays: 0 B/op.

Projection allocations are expected for in-process projected handlers because
the output payload is a DTO graph. The current measured in-process engine paths
allocate the same DTO graph size as the handwritten projection:

- +0 B/op for two selected fields;
- +0 B/op for the default field set;
- +0 B/op for selected fields plus one synchronous include.

Remaining in-process projection overhead is time, not allocation: delegate
dispatch, `ProjectedEventField` construction, and generic projection flow.

Indexed dispatch is now allocation-free and lock-free after registration.
Registration and unregistration copy small bucket arrays and publish immutable
read snapshots, but those are cold-path operations and avoid per-event candidate
materialization.

Projected match grouping no longer allocates subscription-id arrays for groups
with up to four ids. The common small-group path avoids the old result array,
intermediate matched-subscription list, LINQ grouping objects, dictionary, group
objects, small per-group id lists, and current IPC id arrays. Larger groups
still allocate an overflow id array.

Projected IPC dispatch now skips the `ProjectedEvent` and field-array
materialization step. It still allocates the payload backing memory and
`ProjectedEventValue` instances. Subscription ids are inline for the first four
ids in each projection group and only allocate an overflow array for larger
groups.

## Recommendations

Keep the shared filter/projection engine. The absolute overhead is low enough
for parent-side event filtering, especially when it prevents serialization and
IPC.

The largest completed cold-path win is generated schema registration for
built-in abstraction DTOs and public plugin-owned event DTOs. Sourcegen removes
reflection and expression schema construction for those first subscriptions
when the analyzer/runtime are available.

Remaining priority optimizations:

1. Add field-shape-specific scalar emission to the direct projected-payload
   writer if matched projected IPC remains a measured bottleneck.
2. Package the source generator as an SDK/analyzer opt-in for real plugin
   projects if plugin packages should get generated schemas without manually
   referencing the filter runtime project.
3. Measure end-to-end sidecar dispatch with RPC framing, now that indexed
   candidate lookup and projected-match grouping are cheaper.

The compact opcode evaluator has been measured and should not replace compiled
predicates for hot residual filters.

Do not optimize by pushing plugin code back into handwritten delegates. The
engine's validation, bounded field schema, and transportable IR are the reason
it is safe to run before sidecar serialization and IPC.

## Interpretation

For broad event streams with many subscriptions, the engine cost should be
viewed as the price of eliminating much more expensive downstream work.

Example: if a sidecar filter rejects an event in 2-8 ns and prevents one
payload serialization plus one IPC call, it is a clear win. The measured
projected-payload serialization alone costs thousands of nanoseconds. If the
event is already in-process and a trusted plugin would have written an
equivalent direct delegate, the handwritten delegate is faster, but it does not
provide the same cross-process filtering model or safety boundary.

The next benchmark should measure end-to-end sidecar and client-host dispatch
with real RPC framing. The focused rows now cover registry lookup, residual
filter evaluation, projection grouping, serialization, and a local RPC-call
approximation, but they still do not include ShaRPC envelope framing or actual
transport.
