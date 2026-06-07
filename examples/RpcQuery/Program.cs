using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;

// Simulates a remote client sending a filter expression over RPC (like GraphQL where clause)
// Server receives the declarative expression, compiles it, and evaluates against a dataset

var products = new Product[]
{
    new() { Id = 1, Name = "Laptop Pro", Category = "electronics", Price = 1299.99, InStock = true },
    new() { Id = 2, Name = "USB-C Cable", Category = "electronics", Price = 12.99, InStock = true },
    new() { Id = 3, Name = "Standing Desk", Category = "furniture", Price = 499.00, InStock = false },
    new() { Id = 4, Name = "Mechanical Keyboard", Category = "electronics", Price = 159.99, InStock = true },
    new() { Id = 5, Name = "Office Chair", Category = "furniture", Price = 849.00, InStock = true },
    new() { Id = 6, Name = "Monitor 4K", Category = "electronics", Price = 599.00, InStock = false },
};

// Client-side: build the query (this would be serialized and sent over the wire)
var clientQuery = FilterExpression.And(
    FilterExpression.Compare("category", FilterOperator.Equal, FilterValue.From("electronics")),
    FilterExpression.Compare("price", FilterOperator.LessThan, FilterValue.From(500.0)),
    FilterExpression.Compare("inStock", FilterOperator.Equal, FilterValue.From(true)));

Console.WriteLine("Client query: category == 'electronics' && price < 500 && inStock == true\n");

// Server-side: compile the untrusted expression and evaluate
var kernel = FilterCompiler.Compile(typeof(Product), clientQuery);
var results = products.Where(p => kernel.Matches(p)).ToList();

Console.WriteLine($"Results ({results.Count} matches):");
foreach (var product in results)
    Console.WriteLine($"  [{product.Id}] {product.Name} - ${product.Price}");

// Second query: "in" operator (like SQL IN)
Console.WriteLine("\n--- Second query: category IN ('furniture') ---\n");

var furnitureQuery = FilterExpression.In("category", [FilterValue.From("furniture")]);
var furnitureKernel = FilterCompiler.Compile(typeof(Product), furnitureQuery);
var furnitureResults = products.Where(p => furnitureKernel.Matches(p)).ToList();

Console.WriteLine($"Results ({furnitureResults.Count} matches):");
foreach (var product in furnitureResults)
    Console.WriteLine($"  [{product.Id}] {product.Name} - ${product.Price} (inStock={product.InStock})");

public sealed record Product : IFilterSubject
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public double Price { get; init; }
    public bool InStock { get; init; }
}
