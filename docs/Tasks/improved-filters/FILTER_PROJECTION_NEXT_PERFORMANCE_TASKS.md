# Filter And Projection Next Performance Tasks

Date: 2026-06-06.

This task list tracks the next performance work after commit `58cf4aefc`
(`perf(filters): reduce dispatch and projection overhead`). Each completed
task should be benchmarked, documented in `FILTER_PROJECTION_BENCHMARKS.md`,
validated with focused tests/builds, checked off here, and committed as its own
coherent slice.

## Rules

- Prefer one completed task per commit.
- Run the benchmark after each runtime performance change:

```powershell
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

- Keep filter hot paths allocation-free after registration.
- Keep non-generated C# files under the local 300 line target.
- Preserve fallback support for safe plugin-owned DTOs.
- Treat benchmark regressions as blockers unless they are clearly unrelated
  measurement noise and called out in the doc.

## Tasks

- [x] Add full projected dispatch benchmark coverage. Measure the combined
  path across registry candidate lookup, residual filter evaluation, projected
  match grouping, projection materialization, MessagePack payload serialization,
  and a local RPC-call approximation for both server and client bridge shapes.
- [x] Integrate direct projected-payload writers when the full dispatch
  benchmark confirms serialization/materialization is the dominant matched
  delivery cost. Keep projected wire shape centralized so DTO and writer cannot
  diverge.
- [x] Add composed typed projection delegates for common no-include projection
  shapes so selected/default projections avoid per-field generic loop and
  delegate dispatch where schema data can build one compiled projector.
- [x] Evaluate and, if worthwhile, reduce projected dispatch subscription-id
  allocation. Replaced the projected dispatch `string[]` RPC shape with an
  inline subscription-id batch so common small projection groups do not allocate
  id arrays before IPC.
- [x] Extend or design source-generated schemas for plugin-owned DTOs. Built-in
  abstraction DTOs stay generated in the filter runtime; public plugin-owned
  `IGameEvent` DTOs now get a current-assembly generated provider when the
  analyzer and filter runtime are present, while abstraction-only plugin
  assemblies keep fallback discovery.
