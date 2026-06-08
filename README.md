# SiftQL

[![CI](https://github.com/JKamsker/SiftQL/actions/workflows/ci.yml/badge.svg)](https://github.com/JKamsker/SiftQL/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SiftQL.svg)](https://www.nuget.org/packages/SiftQL)

Type-safe event filtering, projection, and subscription routing for .NET.

SiftQL lets you define filters as data (serializable expressions), compile them into fast evaluators at runtime, and route events to matching subscribers -- all with a LINQ-style API backed by a source generator.

## Install

```bash
dotnet add package SiftQL
```

## Quick Start

### Filtering

Define a filter with LINQ expressions and evaluate it against any object:

```csharp
using SiftQL;

var kernel = QueryKernel.For<OrderPlacedEvent>()
    .Where(e => e.Region == "EU" && e.Total > 100.0);

var compiled = FilterCompiler.Compile(typeof(OrderPlacedEvent), kernel.Filter);

compiled.Matches(order); // true or false
```

Filters are plain data -- serialize them, store them in a database, or send them over the wire.

### Projection

Select only the fields you need (GraphQL-style field selection):

```csharp
var kernel = QueryKernel.For<UserActivity>()
    .Where(e => e.Action == "purchase" && e.Amount > 50.0)
    .Select(e => e.UserId, e => e.Action, e => e.Amount);
```

### Subscription Routing

Route events to subscribers based on their filter criteria:

```csharp
using SiftQL.Index;

var index = new TypedFilterSubscriptionIndex<Subscription, SensorReading>();

index.Add(alertSub, FilterExpression.Compare(
    "temperature", FilterOperator.GreaterThan, FilterValue.From(80.0)));

// Find subscriptions whose full filter matches a given event
var matches = index.SnapshotMatches(reading);

// Or use SnapshotCandidates when you only need the fast index candidates.
// Candidate lookup can include false positives for unindexed filters or
// filters that were narrowed by one equality condition.
var candidates = index.SnapshotCandidates(reading);
```

### Serialization

Filters are plain data -- serialize them to JSON, store them in a database, and rehydrate later:

```csharp
using System.Text.Json;
using SiftQL.Expressions;

// Build a filter
var filter = FilterExpression.And(
    FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("EU")),
    FilterExpression.Compare("total", FilterOperator.GreaterThan, FilterValue.From(100.0)));

// Serialize to JSON
string json = JsonSerializer.Serialize(filter);
// Store in DB, send over HTTP, write to a file...

// Deserialize back
FilterExpression restored = JsonSerializer.Deserialize<FilterExpression>(json)!;

// Compile and use
var compiled = FilterCompiler.Compile(typeof(OrderPlacedEvent), restored);
compiled.Matches(order); // works exactly the same
```

The full query pipeline (filter + projection) also round-trips as JSON:

```csharp
var kernel = QueryKernel.For<OrderPlacedEvent>()
    .Where(e => e.Region == "EU" && e.Total > 100.0)
    .Select(e => e.OrderId, e => e.Total);

// Serialize the entire pipeline
string pipelineJson = JsonSerializer.Serialize(kernel.Pipeline);

// Deserialize on the other side
var pipeline = JsonSerializer.Deserialize<EventPipelineExpression>(pipelineJson)!;
```

## How It Works

1. **Define** filters using LINQ expressions or the expression builder API
2. **Compile** filters into optimized evaluators (source-generated for your types)
3. **Evaluate** events against compiled filters at runtime with zero reflection

The source generator (`SiftQL.Generators`) ships inside the `SiftQL` package as an analyzer -- no extra package reference needed.

## Packages

| Package | Description |
|---------|-------------|
| [`SiftQL`](https://www.nuget.org/packages/SiftQL) | Core library + source generator |
| [`SiftQL.Abstractions`](https://www.nuget.org/packages/SiftQL.Abstractions) | Shared interfaces and expression types |

## Requirements

- .NET 10+

## License

MIT
