Yes. I found several things worth fixing or at least validating with regression tests. I did a static review of the uploaded project; I couldn’t run the test suite in this environment because `dotnet` is not installed, so treat this as code-review findings, not test-confirmed failures.

## Highest-priority bug: mutable expressions + cached fingerprints

`FilterExpression` exposes mutable arrays:

`src/Abstractions/Expressions/FilterExpression.cs:44-45`

```csharp
public FilterValue[] Values { get; init; } = [];
public FilterExpression[] Children { get; init; } = [];
```

But fingerprints are cached by object identity in a `ConditionalWeakTable`:

`src/SiftQL/Compiler/FilterExpressionFingerprint.cs:11-17`

```csharp
private static readonly ConditionalWeakTable<FilterExpression, FilterExpressionKey> s_keys = new();

public static FilterExpressionKey CreateKey(FilterExpression expression)
{
    ArgumentNullException.ThrowIfNull(expression);
    return s_keys.GetValue(expression, static item => FilterExpressionKey.From(item));
}
```

That means this kind of mutation can produce stale cache keys:

```csharp
var f = FilterExpression.In("ItemId", [FilterValue.From(1L)]);

_ = FilterCompiler.Compile(typeof(ItemUsedEvent), f); // fingerprint cached for ItemId in [1]

f.Values[0] = FilterValue.From(2L); // legal mutation

var kernel = FilterCompiler.Compile(typeof(ItemUsedEvent), f);
// This may reuse the old fingerprint/cache entry.
```

Same pattern exists for projections:

`src/Abstractions/Expressions/EventProjectionExpression.cs:9-10`

```csharp
public EventProjectionField[] Fields { get; init; } = [];
public EventProjectionInclude[] Includes { get; init; } = [];
```

and:

`src/SiftQL/Projection/ProjectionExpressionFingerprint.cs:12-18`

This is not just a theoretical purity issue. Because `FilterCompiler.CompileCached` uses the fingerprint as part of the cache key, stale fingerprints can return a compiled kernel for the old expression.

**Fix options:**

Use `ImmutableArray<T>` or private arrays with defensive copies. Also avoid exposing mutable arrays publicly. Example direction:

```csharp
public IReadOnlyList<FilterValue> Values => _values;
private readonly FilterValue[] _values;
```

or remove the `ConditionalWeakTable` fingerprint cache and recompute structurally every time. Given this is a query compiler, I’d prefer making the expressions truly immutable.

## Schema registration cache invalidation bug

`FilterSchema` caches schemas by subject type:

`src/SiftQL/Schema/FilterSchema.cs:9,32-35`

```csharp
private static readonly ConcurrentDictionary<Type, FilterSchema> s_cache = new();

public static FilterSchema For(Type subjectType)
{
    ArgumentNullException.ThrowIfNull(subjectType);
    return s_cache.GetOrAdd(subjectType, Build);
}
```

But `RegisterValueObject` only adds the value object type:

`src/SiftQL/Schema/FilterSchema.cs:13-19`

```csharp
public static void RegisterValueObject<T>() => s_valueObjects.TryAdd(typeof(T), 0);
```

Fallback schema discovery only expands registered value objects while the schema is being built:

`src/SiftQL/Schema/FilterSchema.cs:124-129`

```csharp
if (s_valueObjects.ContainsKey(scalarType))
{
    fields.Add(BuildField(name, scalarType, FilterFieldKind.Object, propertyExpression, parameter));
    if (Nullable.GetUnderlyingType(propertyType) is null)
        AddProperties(fields, name, scalarType, propertyExpression, parameter, depth + 1);
}
```

So if this happens:

```csharp
_ = FilterSchema.For(typeof(MyEvent));          // built before value object registration
FilterSchema.RegisterValueObject<MyValueObj>();
var schema = FilterSchema.For(typeof(MyEvent)); // cached old schema, nested fields still missing
```

then nested value-object fields never show up for that subject type.

**Fix options:**

Either enforce “all registrations before first schema use” with a guard/exception, or invalidate schemas when registering:

```csharp
public static void RegisterValueObject(Type type)
{
    ArgumentNullException.ThrowIfNull(type);
    if (s_valueObjects.TryAdd(type, 0))
        s_cache.Clear(); // blunt but safe
}
```

A more precise version would track dependencies, but blunt clearing is probably fine unless registrations happen frequently.

## Compile cache hit still does expensive validation/compilation work

`FilterCompiler.CompileCached` builds schema and compiles the interpreted predicate before checking the kernel cache:

`src/SiftQL/Compiler/FilterCompiler.cs:92-93`

```csharp
FilterSchema schema = schemaFactory(subjectType);
_ = FilterInterpretedCompiler.Compile(schema, expression, errorFactory);
```

The actual cache lookup happens later:

`src/SiftQL/Compiler/FilterCompiler.cs:148-154`

```csharp
if (s_kernelCache.TryGetValue(key, out CompiledKernel? cached))
    return cached;
```

So even on a cache hit, you still pay for:

* schema lookup/build path,
* full interpreted compile/validation,
* parameter scan,
* fingerprint/key creation,
* policy creation.

This makes the cache much less useful for repeated `Compile` calls. It’s especially painful if filters are compiled per subscription/request instead of precompiled once.

**Fix direction:**

Separate validation from compilation, or cache the validation result. At minimum, move interpreted compilation to the cache-miss path for non-parameterized expressions.

One subtlety: custom `errorFactory` behavior may currently depend on validation happening on every call. But for performance, that’s a bad tradeoff on hot paths.

## Nullable reference-type value objects can still cause `NullReferenceException`

The fallback schema only checks `Nullable<T>`:

`src/SiftQL/Schema/FilterSchema.cs:127-128`

```csharp
if (Nullable.GetUnderlyingType(propertyType) is null)
    AddProperties(fields, name, scalarType, propertyExpression, parameter, depth + 1);
```

That does not detect nullable reference types like:

```csharp
public PlayerLocation? Location { get; init; }
```

The source generator has the same problem:

`src/Generators/Schema/SchemaFieldDiscovery.cs:97-99`

```csharp
private static bool IsNullable(ITypeSymbol type) =>
    type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
```

Then both generated and fallback accessors use direct property chains:

`src/SiftQL/Compiler/FilterExpressionCompiler.cs:205-208`

```csharp
foreach (string segment in path.Split('.'))
    current = Expression.PropertyOrField(current, segment);
```

Generated code does the same kind of direct access:

`src/Generators/Schema/FilterSchemaEmitter.cs:118-121`

```csharp
static subject => ((T)subject).Some.Nested.Path
```

So `Location.MapId` can throw if `Location` is null, instead of returning “missing/null/no match”.

**Fix direction:**

Either:

1. Do not expand nullable reference-type value objects into nested paths, or
2. Generate null-propagating accessors for nested paths.

For a filter DSL, I’d expect this behavior:

```csharp
Location.MapId == 5
```

to return `false` when `Location == null`, not throw.

## Subscription indexes are read-optimized but update-expensive

Both subscription indexes use copy-on-write arrays and rebuild snapshots on every add/remove.

Untyped:

`src/SiftQL/Index/FilterSubscriptionIndex.cs:38-82,130-160,178-196`

Typed:

`src/SiftQL/Index/TypedFilterSubscriptionIndex.cs:19-40,107-137,153-173`

For example, every update publishes a new snapshot and clones dictionaries/arrays:

```csharp
Volatile.Write(ref _snapshot, new Snapshot(_unindexed, fields, _count));
```

and removal does things like:

```csharp
foreach (var pair in _byValue.ToArray())
```

This is fine if the design is “many reads, rare subscription changes”. It will hurt badly if subscriptions churn often, for example temporary per-client filters, short-lived rooms, live query updates, or reconnect storms.

**Fix direction:**

Keep this if your workload is read-heavy. If churn matters, consider:

* mutable `List<T>`/`HashSet<T>` under lock,
* immutable collections with structural sharing,
* batching add/remove and publishing snapshots periodically,
* maintaining per-field counts incrementally instead of cloning everything.

## `Contains` silently returns false for collections over 256 items

Runtime contains has a hard cap:

`src/SiftQL/Values/FilterValues.cs:89-106`

```csharp
if (actual is ICollection collection && collection.Count > MaxRuntimeArrayItems)
    return false;

int seen = 0;
bool found = false;
foreach (object? item in enumerable)
{
    if (++seen > MaxRuntimeArrayItems)
        return false;
    if (!found && AreEqual(item, expected))
        found = true;
}
```

Typed array contains does the same:

`src/SiftQL/Schema/FilterArrayContains.cs:5-10`

```csharp
private const int MaxRuntimeArrayItems = 256;

if (items is null || items.Length > MaxRuntimeArrayItems)
    return false;
```

This may be intentional as a guardrail, but semantically it is surprising: a collection with 257 items returns `false` even if the expected value exists at index 0.

**Fix direction:**

Either document it very loudly, or fail validation/runtime with a clear exception, or make the cap configurable. Silent false negatives are dangerous in a query engine.

## Default projections can bypass the `MaxFields` limit

Projection validation checks explicit field count before default field expansion:

`src/SiftQL/Projection/ProjectionCompiler.cs:49-53`

```csharp
projection ??= EventProjectionExpression.Default;
if (projection.Fields.Length > MaxFields)
    throw Error(errorFactory, $"Projection exceeds the {MaxFields} field limit.");
```

But default projection expands later:

`src/SiftQL/Projection/ProjectionCompiler.cs:144-150`

```csharp
EventProjectionField[] requested = projection.Fields.Length == 0
    ? schema.FieldNames
        .Where(name => IsDefaultProjectionField(schema, name))
        .Order(StringComparer.OrdinalIgnoreCase)
        .Select(static name => new EventProjectionField(name))
        .ToArray()
    : projection.Fields;
```

There is no second `requested.Length > MaxFields` check after expansion. So a type with many public scalar/array fields can exceed the intended limit through the default projection path.

**Fix:**

After `requested` is built:

```csharp
if (requested.Length > MaxFields)
    throw Error(errorFactory, $"Projection exceeds the {MaxFields} field limit.");
```

## Payload projection still allocates/copies per event

`ProjectedPayloadWriter` reuses a thread-static `ArrayBufferWriter<byte>`, which is good:

`src/SiftQL/Projection/ProjectedPayloadWriter.cs:260-277`

But it always returns a copied `byte[]`:

`src/SiftQL/Projection/ProjectedPayloadWriter.cs:279-285`

```csharp
byte[] payload = buffer.WrittenSpan.ToArray();
```

That means every `ProjectPayloadAsync` allocates one final payload array and copies the entire MessagePack payload. This is probably necessary for the current `ReadOnlyMemory<byte>` API, because returning memory backed by a reused thread-static buffer would be unsafe.

**Optimization direction:**

Add a lower-level overload that writes into caller-owned memory:

```csharp
void ProjectPayload(object subject, TContext context, IBufferWriter<byte> writer, MessagePackSerializerOptions options)
```

Then high-throughput callers can avoid the copy.

## Provider registration disposal can clear caches unnecessarily

`PrecompiledTieredProviderRegistry.Registration.Dispose` always increments the global version:

`src/SiftQL/Hot/PrecompiledTieredProviderRegistry.cs:213-221`

```csharp
public void Dispose()
{
    lock (s_gate)
    {
        s_providers = s_providers.Where(item => !ReferenceEquals(item, provider)).ToArray();
    }

    IncrementGlobalVersion();
}
```

Calling `Dispose()` twice on the same registration will increment the version twice, even though the second call removes nothing. `IncrementGlobalVersion()` triggers `Changed`, and `FilterCompiler` clears its kernel cache on that event:

`src/SiftQL/Compiler/FilterCompiler.cs:17-20`

```csharp
PrecompiledTieredProviderRegistry.Changed += ClearCache;
```

So double-dispose or redundant removal can cause pointless cache flushes.

**Fix:**

Make registrations idempotent and only increment when the provider list actually changed.

## Smaller bugs / polish

`FilterExpression.And/Or` null child handling is inconsistent. `Not` checks for null, but `Combine` will throw a `NullReferenceException` here:

`src/Abstractions/Expressions/FilterExpression.cs:91-94`

```csharp
var filtered = children
    .Where(static child => child.Kind != FilterExpressionKind.Any)
    .ToArray();
```

Should explicitly reject null children.

`FilterExpression.In` also copies values without checking for null elements:

`src/Abstractions/Expressions/FilterExpression.cs:104-112`

That can later fail in validation with less helpful exceptions.

`HotCompilationManifestWriter.FlushQueued` blocks a ThreadPool thread with `Thread.Sleep`:

`src/SiftQL/Hot/HotCompilationManifestWriter.cs:143-149`

```csharp
if (_options.CoalesceDelay > TimeSpan.Zero)
    Thread.Sleep(_options.CoalesceDelay);
```

The default delay is 50ms:

`src/SiftQL/Hot/HotCompilationManifestWriterOptions.cs:5-7`

A timer or async delay would be cleaner, especially if many writer instances exist.

`ConcurrentDictionary.Count` is used on hot compile/cache paths:

`src/SiftQL/Compiler/FilterCompiler.cs:151`

```csharp
if (s_kernelCache.Count >= MaxCachedKernels)
```

and:

`src/SiftQL/Parameterized/ParameterizedFilterPlanCache.cs:35`

```csharp
if (s_plans.Count >= MaxCachedPlans)
```

`ConcurrentDictionary.Count` is not free under concurrency and the check is racy anyway. A separate approximate counter, bounded LRU, or `MemoryCache`-style eviction would be better.

## What I’d fix first

The two real correctness risks are:

1. **Mutable expression arrays + identity-cached fingerprints.** This can produce wrong cached filters/projections.
2. **Schema cache invalidation after `RegisterValueObject`.** This creates order-dependent missing fields.

Then I’d fix the compile-cache-hit path, because currently repeated `Compile` calls still do too much work before hitting the cache.
