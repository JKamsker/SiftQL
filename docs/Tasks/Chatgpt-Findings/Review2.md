Yes. I see a few **real correctness traps** and several **performance issues** worth fixing. I could not run the test suite because the container here has no .NET SDK installed, so this is a static review of the extracted source.

## Highest-priority correctness issues

### 1. `FilterExpression.Or(...)` handles `Any` incorrectly

In `src/Abstractions/Expressions/FilterExpression.cs:81-103`, both `And` and `Or` go through the same `Combine(...)` helper. That helper removes `Any` children:

```csharp
var flattened = expressions
    .Where(child => child.Kind != FilterExpressionKind.Any)
    ...
```

That is correct for `AND`:

```csharp
x AND Any == x
```

But it is wrong for `OR`:

```csharp
x OR Any == Any
```

Current behavior means this:

```csharp
FilterExpression.Or(FilterExpression.Any, someCondition)
```

silently becomes:

```csharp
someCondition
```

That is a real semantic bug. Fix by giving `And` and `Or` separate normalization logic. For `Or`, if any child is `Any`, return `Any` immediately.

---

### 2. Subscription index appears to return “candidates”, but docs/examples imply “matches”

This is probably the biggest product-level correctness footgun.

The README and example language say things like “Find all subscriptions that match a given event”. The example in `examples/Subscriptions/Program.cs:8-17` registers filters like:

```csharp
temperature > 80
zone == "A" && pressure > 150
```

But the index only extracts one exact scalar equality from a filter. In `src/SiftQL/Index/FilterIndexExtractor.cs:30-59`, `And(...)` picks one “best” equality and ignores the rest. Non-equality filters become unindexed.

Then `src/SiftQL/Index/FilterSubscriptionIndex.cs:106-115` returns:

```csharp
_unindexed + bucket candidates
```

without evaluating the original filter.

So these become false positives:

```csharp
temperature > 80
```

This is unindexed and returned for every event.

```csharp
zone == "A" && pressure > 150
```

This is indexed by `zone == "A"`, but the `pressure > 150` part is not checked by the index.

That is fine if the API is explicitly “candidate lookup only”, but the docs/examples currently make it look like final matching. I would either rename/clarify aggressively, or store the compiled filter with each subscription and expose something like:

```csharp
SnapshotCandidates(...)
SnapshotMatches(...)
ForEachMatch(...)
```

where `Matches` runs the compiled predicate after candidate lookup.

---

### 3. Nested property access can throw `NullReferenceException`

There are multiple places where nested property paths are compiled as direct member chains without null guards.

Examples:

`src/SiftQL/Compiler/FilterExpressionCompiler.cs:202-209`:

```csharp
current = Expression.PropertyOrField(current, segment);
```

`src/SiftQL/Index/FilterIndexValueAccessor.cs:150-172` does the same kind of direct traversal.

`src/SiftQL/Projection/ProjectionPayloadWriterCompiler.cs:57-73` also walks nested properties directly.

The runtime schema builder tries to avoid nullable nested value objects in `src/SiftQL/Schema/FilterSchema.cs:131-148`, and the generator also checks nullable annotations. That helps, but it is not a full runtime guarantee. If a property is declared non-nullable but is actually null at runtime, which happens easily with deserialization, bad input, `default!`, partially initialized objects, or legacy models, nested filters/projections can crash.

Example risk:

```csharp
public Location Location { get; init; } = null!;
```

A filter on:

```csharp
location.country == "AT"
```

can throw when `Location` is null.

For query engines, I would strongly prefer null-propagating semantics:

```csharp
location.country == "AT" // false if location is null
```

rather than throwing.

---

## Performance issues

### 4. Subscription index updates are expensive under churn

The index is optimized for lock-free/read-mostly lookup, but add/remove churn is costly.

In `src/SiftQL/Index/FilterSubscriptionIndex.cs:130-160`, every add/remove copies arrays and republishes a snapshot. `FieldIndex.Remove` in `src/SiftQL/Index/FilterSubscriptionIndex.cs:178-190` does this:

```csharp
foreach (var pair in _byValue.ToArray())
```

That clones all dictionary entries just to remove one subscription.

The typed version has similar behavior in `src/SiftQL/Index/TypedFilterSubscriptionIndex.cs:107-138` and `src/SiftQL/Index/TypedFilterSubscriptionIndex.cs:153-165`.

This is fine if subscriptions are mostly static. It is bad if clients frequently connect/disconnect, change filters, resubscribe, or use ephemeral subscriptions.

I would add a reverse map:

```csharp
subscriptionId -> indexed field/value bucket
```

Then removal does not need to scan all buckets. Also consider batching snapshot publication if many subscriptions are added at once.

---

### 5. `ProjectedEvent` field lookup is linear

`src/Abstractions/Projected/ProjectedEvent.cs:24-40` does field lookup by scanning the field array case-insensitively.

That means a projected filter with several field reads does:

```text
predicate count × field count
```

string comparisons.

Then `src/SiftQL/Projection/ProjectedEventFilterSchema.cs:64-91` converts projected values back to objects. Arrays allocate via:

```csharp
value.Values.Select(ToObject).ToArray()
```

For small events this is fine. For high-volume event pipelines, this can become a hot path.

Possible fixes:

```csharp
ProjectedEvent
  -> lazy dictionary name -> index/value

or

compiled projected filters
  -> use known field index mapping instead of name lookup
```

If projected events are expected to be tiny, this is acceptable. If this library is meant for high-throughput routing/filtering, I would benchmark this path.

---

### 6. `FilterValues.Contains` does not return early after a match

In `src/SiftQL/Values/FilterValues.cs:89-107`, the fallback `Contains` implementation keeps enumerating even after finding a match:

```csharp
if (StringEquals(...))
{
    found = true;
}
```

It does not immediately return. It continues so it can enforce `MaxRuntimeArrayItems`.

That means this:

```csharp
Contains(hugeEnumerable, "first-item")
```

still walks the whole enumerable.

There is even a test named like it expects early-return behavior: `Wave1BugRegressionTests.cs` has `ContainsFallbackReturnsEarlyOnFirstMatch`, but the implementation does not actually return early.

Typed array contains helpers do early-return, but the generic `IEnumerable` fallback does not.

You need to decide which invariant matters more:

```text
A) successful match should return immediately
B) oversized enumerable should always be detected, even if match was early
```

For performance, I would return early when the source has a known safe count, for example `ICollection.Count <= MaxRuntimeArrayItems`.

---

### 7. Cache caps degrade permanently after filling up

There are several capped caches that stop caching new entries once full.

Examples:

`src/SiftQL/Compiler/FilterCompiler.cs:14-16`:

```csharp
private const int MaxCachedKernels = 4096;
```

Later, once the cap is hit, new filters compile uncached.

Similar pattern:

`src/SiftQL/Projection/EventPipelineCompiler.cs:15-17`

`src/SiftQL/Parameterized/ParameterizedFilterPlanCache.cs:12-14`

This avoids unbounded memory growth, which is good. But under multi-tenant or ad-hoc query workloads, a burst of one-off filters can fill the cache. After that, the real hot working set may never be cached.

Better options:

```text
LRU / approximate LRU
MemoryCache with size limit
segmented cache
manual cache clear / metrics
per-tenant cache partitioning
```

The parameterized plan cache has some metrics, but the kernel/pipeline caches appear less observable.

---

### 8. Projection includes are awaited sequentially

In `src/SiftQL/Projection/CompiledProjection.cs:146-218`, includes are processed one after another.

That is okay if includes are CPU-local or depend on each other. But if include projectors do independent I/O, for example DB lookups or service calls, latency adds up:

```text
include1 20ms
include2 20ms
include3 20ms
=> 60ms instead of roughly 20ms
```

I would either document that includes should be batched upstream, or add an optional concurrent mode using `Task.WhenAll` with throttling and deterministic result ordering.

---

### 9. Payload writer always copies the generated payload

`src/SiftQL/Projection/ProjectedPayloadWriter.cs:260-285` reuses a thread-static `ArrayBufferWriter<byte>`, which is good. But then `CopyWrittenPayload` does:

```csharp
buffer.WrittenSpan.ToArray()
```

So every projected payload allocates a fresh `byte[]` and copies the full payload.

That may be necessary for the current API because the backing buffer is reused, but it is still a hot-path allocation.

For high-throughput usage, expose an overload like:

```csharp
WriteTo<T>(T source, IBufferWriter<byte> destination)
```

or let callers provide/recycle buffers explicitly.

---

### 10. Object-to-projected-value conversion has an unbounded type cache

`src/Abstractions/Projected/ProjectedEventValue.cs:28` has:

```csharp
private static readonly ConcurrentDictionary<Type, PropertyInfo[]> s_objectProperties = new();
```

Then `FromObjectValue` reflects arbitrary object types and caches their public properties.

That is fine for known DTO types. It can become a memory leak shape if arbitrary plugin-generated, anonymous, dynamic, or runtime-emitted types flow through this path.

Also, property getters are invoked reflectively. If a getter is expensive, throws, or has side effects, projection conversion inherits that behavior.

I would either bound this cache, restrict it to known/schema-registered types, or make arbitrary object projection opt-in.

---

## Smaller issues / code smells

### Duplicate condition in translator

In `src/Abstractions/Translation/KernelExpressionTranslator.cs:273-281`, both `IsKernelIn` and `IsKernelExists` repeat:

```csharp
method.DeclaringType == typeof(QueryKernelPredicates)
```

twice. It looks harmless, but probably indicates a copy/paste mistake or missing intended declaring type.

---

### Public array properties undermine immutability

Several expression records expose arrays directly:

`src/Abstractions/Expressions/FilterExpression.cs:44-45`:

```csharp
public FilterValue[] Values { get; init; }
public FilterExpression[] Children { get; init; }
```

Similar shape exists in projection/pipeline expressions.

Records with public arrays look immutable, but callers can mutate the arrays after construction. That can invalidate compiled assumptions, index state, fingerprints, or equality expectations.

I would prefer:

```csharp
ImmutableArray<T>
```

or defensive-copy arrays on construction and expose `IReadOnlyList<T>`/`ReadOnlyMemory<T>`.

---

## What I would fix first

I would prioritize these in this order:

1. **Fix `FilterExpression.Or(Any, x)` semantics.** That is a direct correctness bug.
2. **Clarify/fix subscription “candidate” vs “match” behavior.** This can cause wrong subscribers to receive events.
3. **Add null-propagation tests for nested property paths.** Decide whether null should mean “no match” or “throw”, then make it consistent.
4. **Optimize subscription remove/update if subscriptions are dynamic.** The current copy/scan model is only good for read-mostly static indexes.
5. **Benchmark projected-event filtering.** The linear name lookup and object conversion could dominate throughput for high event volume.

Overall: the code looks pretty deliberate and performance-aware, but there are a few spots where the API semantics are more dangerous than the implementation itself. The subscription index especially needs clearer separation between “fast candidate narrowing” and “actual filter match.”
