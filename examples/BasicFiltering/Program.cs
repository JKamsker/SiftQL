using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;

var order = new OrderPlacedEvent
{
    OrderId = Guid.NewGuid(),
    CustomerId = 42,
    Total = 199.99,
    Currency = "EUR",
    Region = "EU",
};

var filter = FilterExpression.And(
    FilterExpression.Compare("region", FilterOperator.Equal, FilterValue.From("EU")),
    FilterExpression.Compare("total", FilterOperator.GreaterThan, FilterValue.From(100.0)));

var kernel = FilterCompiler.Compile(typeof(OrderPlacedEvent), filter);

Console.WriteLine($"Order {order.OrderId}");
Console.WriteLine($"  Region={order.Region}, Total={order.Total}");
Console.WriteLine($"  Matches filter: {kernel.Matches(order)}");

var smallOrder = order with { Total = 50.0 };
Console.WriteLine($"\nSmall order (Total={smallOrder.Total})");
Console.WriteLine($"  Matches filter: {kernel.Matches(smallOrder)}");

var usOrder = order with { Region = "US" };
Console.WriteLine($"\nUS order (Region={usOrder.Region})");
Console.WriteLine($"  Matches filter: {kernel.Matches(usOrder)}");

// LINQ expression builder
var typedKernel = QueryKernel.For<OrderPlacedEvent>()
    .Where(e => e.Region == "EU" && e.Total > 100.0);

Console.WriteLine("\n--- LINQ-style kernel ---");
var compiled = FilterCompiler.Compile(typeof(OrderPlacedEvent), typedKernel.Filter);
Console.WriteLine($"  EU order matches:  {compiled.Matches(order)}");
Console.WriteLine($"  US order matches:  {compiled.Matches(usOrder)}");
Console.WriteLine($"  Small order matches: {compiled.Matches(smallOrder)}");

public sealed record OrderPlacedEvent : IFilterSubject
{
    public Guid OrderId { get; init; }
    public long CustomerId { get; init; }
    public double Total { get; init; }
    public string Currency { get; init; } = "";
    public string Region { get; init; } = "";
}
