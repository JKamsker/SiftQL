# Feature Request: First-class query context contracts and generated descriptors

## Summary

SiftQL already supports context-aware query kernels through
`QueryKernel<TSubject, TContext>` and expression forms such as:

```csharp
var query = QueryKernel.For<OrderEvent>()
    .WithContext<OrderEvent, IOrderQueryContext>()
    .Where(static (ev, ctx) => ctx.Customer(ev.CustomerId).IsActive)
    .Select(static (ev, ctx) => new
    {
        ev.OrderId,
        CustomerTier = ctx.Customer(ev.CustomerId).Tier,
    });
```

That shape is valuable for distributed/event-driven systems where a subscriber
wants to filter or project using endpoint-local data without shipping that data
to every subscriber.

The missing piece is a first-class contract model for `TContext`. Today a
consumer can pass any type as `TContext`, and SiftQL lowers context method calls
into projection includes using only the method name and member path. Application
runtimes then have to duplicate method names, argument rules, member projection
rules, and provider bindings by hand.

This issue proposes adding an optional query-context contract model backed by an
incremental source generator:

- context interfaces can be marked as SiftQL query contexts;
- each context contract receives a stable context id;
- context methods receive stable intrinsic ids;
- generated descriptors describe methods, parameters, return types, and generated
  include names;
- generated helper APIs create typed `With...Context()` extension methods and
  manual include factories;
- SiftQL context intrinsics can include the context identity, not only the method
  name.

The goal is to keep SDK/user authoring strongly typed while giving runtimes a
small generated manifest they can use for validation and endpoint-side projection
binding.

## Problem

The current context feature is flexible, but too loose for larger systems.

### Context methods are expression-only but look executable

Many consuming applications model a context with placeholder methods that throw:

```csharp
public sealed class OrderQueryContext
{
    public CustomerSnapshot Customer(long customerId) =>
        throw new NotSupportedException("Only available in SiftQL expressions.");
}
```

This gives the desired SDK/user syntax, but the type is misleading:

- it is not a real runtime service;
- it is not implemented by the endpoint;
- it has no stable contract id;
- invalid method/member usage is discovered only when the runtime compiler sees
  the generated projection include;
- the application must maintain separate string constants and switch statements
  that mirror the method signatures.

Interfaces would better express the intent:

```csharp
[SiftQueryContext("orders.server")]
public interface IOrderQueryContext
{
    CustomerSnapshot Customer(long customerId);

    IReadOnlyList<RecentOrderSnapshot> RecentOrders(
        long customerId,
        int limit);
}
```

The interface is a public query contract. The endpoint can implement a separate
provider for actual data access, and SiftQL can use the interface for expression
translation and manifest generation.

### Intrinsics are not context-qualified

Context method calls currently lower to method-name based intrinsics, roughly:

```text
siftql.context.method:Customer.Tier
```

That works while each pipeline has only one context type and method names remain
unique. It is weaker than it needs to be:

- two context contracts can both have `Customer(...)`;
- a runtime compiler cannot tell from the intrinsic alone which context contract
  produced it;
- application code has to compare method names rather than stable method ids;
- old pipelines can silently bind to new behavior if a method is renamed or
  overloaded carelessly.

A context-qualified intrinsic is safer:

```text
siftql.context:orders.server.method:customer.tier
```

The exact string format is less important than the properties:

- contains a stable context id;
- contains a stable method id;
- preserves the member path;
- remains serializable as plain SiftQL data;
- can be parsed without loading application-specific types.

### Runtime include compilers duplicate public contract details

Endpoint runtimes typically need to do all of this by hand:

- recognize a context method intrinsic;
- validate required arguments;
- distinguish source-field arguments from literal arguments;
- validate numeric bounds or optional defaults;
- map a context method to a provider method;
- project a scalar member path from the returned DTO;
- require capabilities/permissions before accepting the pipeline.

Most of that can be generated from a declared context contract plus small
application-specific binding code.

## Goals

1. Make query contexts explicit public contracts.
2. Make context method intrinsics stable and context-qualified.
3. Generate descriptors from attributed context interfaces using an incremental
   source generator.
4. Keep generated output additive. The generator should not rewrite user code.
5. Keep endpoint execution application-owned. SiftQL should not attempt to call
   remote services or arbitrary providers by default.
6. Provide enough generated metadata that application runtimes can build strict
   projection include compilers without hand-maintaining method-name constants.
7. Preserve existing `QueryKernel<TSubject, TContext>` behavior and serialized
   pipeline compatibility where possible.

## Non-goals

- Do not execute context methods on the client/subscriber side.
- Do not turn every context method into an RPC call.
- Do not generate endpoint-specific data access code from SiftQL alone.
- Do not scan arbitrary loaded assemblies at runtime.
- Do not require one source generator to consume another generator's generated
  source in the same compilation.
- Do not support arbitrary method bodies, lambdas, delegates, reflection calls,
  or expression nodes inside context contracts.

## Proposed API

### Attributes

Add attributes to `SiftQL.Abstractions`:

```csharp
namespace SiftQL;

[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class SiftQueryContextAttribute : Attribute
{
    public SiftQueryContextAttribute(string id)
    {
        Id = id;
    }

    public string Id { get; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class SiftQueryContextMethodAttribute : Attribute
{
    public SiftQueryContextMethodAttribute(string? id = null)
    {
        Id = id;
    }

    public string? Id { get; }
}
```

Optional later additions:

```csharp
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class SiftQueryDefaultValueAttribute : Attribute
{
    public SiftQueryDefaultValueAttribute(object? value)
    {
        Value = value;
    }

    public object? Value { get; }
}
```

The first version does not need default-value attributes if ordinary C# optional
parameters are sufficient.

### Descriptor types

Add small descriptor types to `SiftQL.Abstractions`:

```csharp
public sealed record SiftQueryContextDescriptor(
    Type ContextType,
    string ContextId,
    IReadOnlyList<SiftQueryContextMethodDescriptor> Methods);

public sealed record SiftQueryContextMethodDescriptor(
    string MethodName,
    string MethodId,
    Type ReturnType,
    IReadOnlyList<SiftQueryContextParameterDescriptor> Parameters);

public sealed record SiftQueryContextParameterDescriptor(
    string Name,
    Type Type,
    bool HasDefaultValue,
    object? DefaultValue);
```

Descriptors should favor simple strings, `Type` objects, primitive values, and
arrays. They should not contain Roslyn symbols, syntax nodes, locations, or
runtime provider instances.

### Intrinsic helpers

Extend `EventProjectionContextIntrinsics` with context-qualified methods:

```csharp
public static string Method(
    string contextId,
    string methodId,
    string memberPath);

public static bool TryParseMethod(
    string intrinsic,
    out string contextId,
    out string methodId,
    out string memberPath);
```

Keep the existing method-name-only parse path for compatibility:

```csharp
public static bool TryParseLegacyMethod(
    string intrinsic,
    out string methodName,
    out string memberPath);
```

or keep the existing overload and add a new one. The important part is that
application runtimes can opt into the stronger form without breaking old
serialized pipelines.

### Generated helper shape

For this context:

```csharp
namespace Sample.Contracts;

[SiftQueryContext("orders.server")]
public interface IOrderQueryContext
{
    [SiftQueryContextMethod("customer")]
    CustomerSnapshot Customer(long customerId);

    [SiftQueryContextMethod("recent-orders")]
    IReadOnlyList<RecentOrderSnapshot> RecentOrders(long customerId, int limit);
}
```

the generator could emit:

```csharp
// <auto-generated />
#nullable enable

namespace Sample.Contracts;

public static class OrderQueryContextSiftQlExtensions
{
    public const string ContextId = "orders.server";
    public const string CustomerMethodId = "customer";
    public const string RecentOrdersMethodId = "recent-orders";

    public static global::SiftQL.QueryKernel<TSubject, IOrderQueryContext>
        WithOrderQueryContext<TSubject>(
            this global::SiftQL.QueryKernel<TSubject> kernel)
    {
        global::System.ArgumentNullException.ThrowIfNull(kernel);
        return kernel.WithContext<TSubject, IOrderQueryContext>();
    }

    public static global::SiftQL.Expressions.EventProjectionInclude Customer(
        global::SiftQL.Expressions.EventProjectionArgument customerId,
        string resultName = "customer")
    {
        return new global::SiftQL.Expressions.EventProjectionInclude(
            global::SiftQL.Expressions.EventProjectionContextIntrinsics.Method(
                ContextId,
                CustomerMethodId,
                memberPath: ""),
            resultName,
            [customerId]);
    }

    public static global::SiftQL.SiftQueryContextDescriptor Descriptor { get; }
        = new(
            typeof(IOrderQueryContext),
            ContextId,
            [
                new(
                    nameof(IOrderQueryContext.Customer),
                    CustomerMethodId,
                    typeof(CustomerSnapshot),
                    [
                        new("customerId", typeof(long), false, null),
                    ]),
                new(
                    nameof(IOrderQueryContext.RecentOrders),
                    RecentOrdersMethodId,
                    typeof(global::System.Collections.Generic.IReadOnlyList<RecentOrderSnapshot>),
                    [
                        new("customerId", typeof(long), false, null),
                        new("limit", typeof(int), false, null),
                    ]),
            ]);
}
```

The exact naming can differ. The core requirement is a stable generated descriptor
and convenience helpers.

## Runtime behavior

When translating:

```csharp
.Where(static (ev, ctx) => ctx.Customer(ev.CustomerId).IsActive)
```

SiftQL should prefer generated/registered metadata for `IOrderQueryContext`:

- context id: `orders.server`
- method id: `customer`
- member path: `IsActive`
- argument `customerId`: source field `CustomerId`

The emitted include can then carry:

```text
intrinsic = "siftql.context:orders.server.method:customer.IsActive"
arguments = [source-field customerId = "CustomerId"]
```

If no generated descriptor is registered, SiftQL can retain today's behavior:

```text
intrinsic = "siftql.context.method:Customer.IsActive"
```

That gives a gradual migration path.

## Descriptor registration options

There are a few implementable options. The issue does not require a specific one.

### Option A: generated static descriptor only

The generator emits `Descriptor` and helper methods. Consuming runtimes call the
descriptor explicitly.

Pros:

- simplest;
- no runtime registration lifecycle;
- no assembly lookup.

Cons:

- SiftQL's expression translator cannot automatically use context-qualified
  intrinsics unless callers pass descriptors or the translator reflects over
  attributes.

### Option B: generated registry plus explicit registration

SiftQL exposes:

```csharp
public static class SiftQueryContextRegistry
{
    public static void Register(SiftQueryContextDescriptor descriptor);
    public static bool TryGet(Type contextType, out SiftQueryContextDescriptor descriptor);
}
```

The generated code exposes:

```csharp
public static void RegisterSiftQueryContexts()
{
    SiftQueryContextRegistry.Register(OrderQueryContextSiftQlExtensions.Descriptor);
}
```

Pros:

- no module initializer required;
- deterministic;
- easy to test;
- applications can call registration in startup code.

Cons:

- forgetting registration falls back to legacy behavior or causes a diagnostic,
  depending on options.

### Option C: generated registry plus module initializer

Generated code registers descriptors automatically.

Pros:

- easiest for applications.

Cons:

- more care needed for target frameworks and trimming;
- harder to make explicit in tests;
- package needs to decide whether automatic registration is acceptable.

Recommendation: start with Option A or B. Avoid a required module initializer in
the first version.

## Incremental generator design

This request is intended to fit normal incremental source generator constraints.

### Discovery

Use:

```csharp
context.SyntaxProvider.ForAttributeWithMetadataName(
    "SiftQL.SiftQueryContextAttribute",
    static (node, _) => node is InterfaceDeclarationSyntax,
    static (ctx, ct) => BuildContextModel(ctx, ct));
```

Do not scan every type in the compilation. Do not inspect referenced assemblies
looking for arbitrary query contexts. If a referenced contract assembly contains
query contexts, that assembly should have run the generator and carry its own
generated descriptor.

### Models

Extract compact value-equatable models early:

```csharp
internal sealed record QueryContextModel(
    string Namespace,
    string InterfaceName,
    string FullyQualifiedInterfaceName,
    string ContextId,
    EquatableArray<QueryContextMethodModel> Methods);

internal sealed record QueryContextMethodModel(
    string Name,
    string MethodId,
    string ReturnTypeDisplay,
    string ReturnTypeTypeofDisplay,
    EquatableArray<QueryContextParameterModel> Parameters);
```

The long-lived model should not contain `ISymbol`, `SyntaxNode`, `Location`, or
mutable arrays. Diagnostics can be represented as small diagnostic models with
location data only at the reporting boundary.

### Validation

Report diagnostics for unsupported context contracts:

- context type is not an interface;
- context interface is generic;
- context id is empty or whitespace;
- context id duplicates another context in the same compilation;
- method is generic;
- method has `ref`, `out`, or `in` parameters;
- method has pointer/function-pointer types;
- method returns `void`;
- method id is empty or duplicates another method id in the same context;
- overloads share the same method id without an explicit override;
- optional parameter defaults cannot be represented as a SiftQL literal;
- generated helper class name collides with an existing type in the same
  namespace.

The generator should emit no descriptor for invalid contexts that have blocking
diagnostics.

### Output

Generated files should be additive:

- one stable hint name per context, for example
  `Sample.Contracts.IOrderQueryContext.SiftQueryContext.g.cs`;
- optional one assembly-level registry file if needed;
- no changes to user source;
- no generated partial members required unless explicitly designed.

### Performance

- Use `ForAttributeWithMetadataName`.
- Avoid `CompilationProvider` except for narrowly scoped collision checks that
  cannot be done from the target symbol.
- Keep syntax predicates cheap.
- Use `StringBuilder` or a small writer to generate source.
- Use fully qualified type names in generated code.
- Pass cancellation tokens through symbol/model building.

## Endpoint-side binding example

SiftQL should not generate this provider, but the generated descriptor makes this
kind of application code much safer:

```csharp
public sealed class OrderQueryProjectionBinder
{
    public IncludeProjector Bind(EventProjectionInclude include)
    {
        if (!EventProjectionContextIntrinsics.TryParseMethod(
                include.Intrinsic,
                out string contextId,
                out string methodId,
                out string memberPath))
        {
            throw new QueryValidationException("Unsupported context include.");
        }

        if (contextId != OrderQueryContextSiftQlExtensions.ContextId)
            throw new QueryValidationException("Unsupported context contract.");

        return methodId switch
        {
            OrderQueryContextSiftQlExtensions.CustomerMethodId =>
                BindCustomer(include, memberPath),
            OrderQueryContextSiftQlExtensions.RecentOrdersMethodId =>
                BindRecentOrders(include, memberPath),
            _ => throw new QueryValidationException("Unsupported context method."),
        };
    }
}
```

The runtime still owns:

- provider dependencies;
- async data access;
- permission/capability checks;
- bounds such as maximum radius, maximum limit, maximum page size;
- serialization and DTO compatibility.

SiftQL only provides the typed authoring, stable lowering, and generated contract
metadata.

## Compatibility

This can be introduced without breaking existing pipelines:

1. Keep current `siftql.context.method:{Method}.{Member}` intrinsics.
2. Add context-qualified intrinsics.
3. Teach parsers to recognize both forms.
4. Make descriptor-backed contexts emit the new form.
5. Allow runtimes to accept both during a migration window.

Consumers that do not use `[SiftQueryContext]` continue to get current behavior.

## Testing

Add focused tests around:

- interface context translation;
- context-qualified intrinsic generation;
- legacy intrinsic compatibility;
- descriptor source output snapshots;
- duplicate context id diagnostics;
- duplicate method id diagnostics;
- unsupported method shapes;
- source-field and literal argument translation;
- member-path translation from context method return values;
- incremental cacheability, verifying equivalent inputs produce equivalent
  models and stable output.

Also add a small integration-style test:

```csharp
[SiftQueryContext("sample.users")]
public interface IUserQueryContext
{
    UserSnapshot User(long userId);
}

var kernel = QueryKernel.For<UserEvent>()
    .WithUserQueryContext()
    .Where(static (ev, ctx) => ctx.User(ev.UserId).IsActive)
    .Select(static (ev, ctx) => new
    {
        ev.EventId,
        Tier = ctx.User(ev.UserId).Tier,
    });
```

Assert that the resulting pipeline contains a projection include with:

- context id `sample.users`;
- method id `User` or the explicit configured id;
- source-field argument `userId = UserId`;
- member paths `IsActive` and `Tier`;
- no duplicate include for repeated identical context calls.

## Acceptance criteria

- Attributed query context interfaces produce generated descriptors and helper
  extensions.
- Context method intrinsics can include a stable context id and method id.
- Existing context pipelines still parse.
- The generator uses `ForAttributeWithMetadataName` and compact equatable models.
- The generator does not rely on runtime state or generated output from another
  generator.
- Invalid contracts produce clear diagnostics.
- Runtimes can bind context includes using generated descriptors rather than
  maintaining parallel string constants.
