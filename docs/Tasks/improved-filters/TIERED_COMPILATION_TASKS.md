# Tiered Compilation Implementation Tasks

Date: 2026-06-06.

This tasklist executes `tiered-compilation.md` in committed layers. Each layer
must include focused unit tests, a targeted benchmark run where practical, a
checked-off task entry, and its own commit.

## Validation Commands

Use focused validation after each layer:

```powershell
dotnet test src/Plugins/Runtime/FourStory.ClientBridge.Core.Tests/FourStory.ClientBridge.Core.Tests.csproj -c Release --no-restore --filter Tiered
dotnet build src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release
dotnet run --project src/Plugins/Runtime/FourStory.Plugin.Filters.Benchmarks/FourStory.Plugin.Filters.Benchmarks.csproj -c Release --no-build -- --samples=9
```

Run broader validation before final handoff:

```powershell
dotnet test src/Plugins/Runtime/FourStory.ClientBridge.Core.Tests/FourStory.ClientBridge.Core.Tests.csproj -c Release --no-restore
dotnet build Plugins.slnf -c Release
```

## Layers

- [x] Layer 1: Add tiered filter runtime skeleton. New filters should register
  as interpreted kernels when tiering is enabled, count evaluations/matches,
  expose promotion state for tests, and still validate synchronously.
- [x] Layer 2: Add bounded off-thread filter expression compilation. Hot
  interpreted kernels should queue compilation after age/evaluation thresholds
  and atomically swap to compiled delegates without re-registering
  subscriptions.
- [x] Layer 3: Add tiered filter benchmark coverage and default policy tuning.
  Measure interpreted, promoted, immediate expression-compiled, and current
  hardcoded filter paths; document the break-even result.
- [x] Layer 4: Add tiered projection runtime skeleton. New projections should
  start with interpreted field/include projectors, count materializations and
  direct payload writes, and preserve projected payload bytes.
- [x] Layer 5: Add off-thread projection promotion. Hot projections should
  compile composed field-array projectors and direct payload writer helpers,
  then swap the active projector/writer in place.
- [x] Layer 6: Add hot filter/projection manifest persistence. Runtime should
  record frequent filters/projections off-thread, coalesce writes, atomically
  replace JSON, and decay stale entries.
- [x] Layer 7: Add precompiled provider contract and startup registration.
  Filter/projection compilers should query registered precompiled providers
  before interpreted/immediate fallback.
- [x] Layer 8: Add JSON-driven hot DLL source generator/build path. The
  generator should read the manifest, emit provider code for known entries, and
  validate schema/runtime/generator versions.
- [x] Layer 9: Add server startup hot DLL loader. The loader should validate
  manifest hash/version data, load the provider, and ignore stale DLLs with
  diagnostics rather than failing startup.
- [x] Layer 10: Add optional runtime batch DLL compilation design hooks. Keep
  `Expression.Compile` as immediate fallback, but allow long-running servers to
  batch newly hot filters into collectible temporary assemblies later.
- [x] Layer 11: Remove promoted-tier holder overhead and add typed runtime
  kernels without sourcegen. Promoted filters should swap the parent
  `CompiledKernel` predicate, promoted projections should swap the parent
  no-include field projector, and generic call sites should be able to use the
  typed compiled predicate.
- [x] Layer 12: Reduce object dispatch in filter hot paths. Add typed
  subscription indexes, typed per-event bridge/server buckets, and cached
  `CompiledKernelMatcher<TSubject>` instances so indexed event routing avoids
  object key extraction and repeated typed-delegate discovery. Keep unfiltered
  `Any` matchers on a typed always-true delegate.
- [x] Layer 13: Add typed projection-context authoring. Support
  `.Select(static (ev, ctx) => new { ... })` on server and client kernels,
  lowering only approved bounded context calls into existing projection
  includes while rejecting arbitrary projection computation.
