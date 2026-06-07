# Filter And Projection Performance Tasks

Date: 2026-06-06.

This task list tracks the next performance improvements for the shared
`FourStory.Plugin.Filters` engine. Each improvement must be implemented,
benchmarked, documented in `FILTER_PROJECTION_BENCHMARKS.md`, validated with
focused tests plus the quick suite where appropriate, and committed separately.

Baseline/current numbers live in
`docs/Tasks/improved-filters/FILTER_PROJECTION_BENCHMARKS.md`.

## Rules

- Commit each completed improvement individually.
- Run the benchmark after every improvement:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

- Record the measurement delta in `FILTER_PROJECTION_BENCHMARKS.md`.
- Keep filters allocation-free on the steady-state hot path.
- Keep non-generated C# files under the local 300 line target.
- Preserve fallback support for safe plugin-owned DTOs.

## Tasks

- [x] Compile whole filters into one typed predicate. Build a single compiled
  predicate that casts the subject once, reads fields directly, and emits
  `and`/`or` short-circuiting without per-clause delegate hops.
- [x] Cache compiled predicates over generated/fallback access metadata. Reuse
  equivalent kernels by subject type and structural filter fingerprint so
  source-generated DTO schemas pay expression compilation once per filter shape.
- [x] Add dispatch indexing before residual filters. Extract exact match keys
  from kernels and route candidate subscriptions by event type, map/channel,
  character/session ids, item ids, skill ids, and similar scalar keys before
  evaluating residual predicates.
- [x] Order predicate clauses by estimated cost and selectivity. Preserve
  semantics while evaluating cheap/high-rejection `and` clauses before more
  expensive array/string/intrinsic work.
- [x] Specialize `in` predicates further. Use unrolled comparisons for small
  typed value sets and prebuilt typed `HashSet<T>` instances for larger sets.
- [x] Remove avoidable nullable and generic accessor overhead. Generate or build
  non-nullable accessor paths where schema proves a field cannot be null.
- [x] Add projection fast paths by shape. Specialize common no-include
  projections such as one field, two fields, and default field sets so they do
  less generic loop work.
- [x] Evaluate a compact opcode residual evaluator. Benchmark a compact
  instruction-array interpreter against nested delegates and whole-expression
  predicates before committing to it as a runtime path.
- [x] Investigate serialization/projection fusion for IPC. For sidecar delivery,
  measure writing projected payloads directly to the serializer path instead of
  first materializing the full intermediate `ProjectedEvent` graph.
