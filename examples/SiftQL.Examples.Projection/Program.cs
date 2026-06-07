using SiftQL;
using SiftQL.Projection;

// Build a kernel that filters + projects (GraphQL-style field selection)
var kernel = QueryKernel.For<UserActivity>()
    .Where(e => e.Action == "purchase" && e.Amount > 50.0)
    .Select(e => e.UserId, e => e.Action, e => e.Amount);

// Compile the pipeline
var pipeline = EventPipelineCompiler.Compile<EmptyContext>(
    typeof(UserActivity),
    kernel.Pipeline,
    compileInclude: static (_, include) => throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'."),
    EventPipelineCompilerOptions.Immediate);

var activities = new UserActivity[]
{
    new() { UserId = "u1", Action = "purchase", Amount = 120.0, IpAddress = "10.0.0.1", UserAgent = "Mozilla/5.0" },
    new() { UserId = "u2", Action = "browse", Amount = 0, IpAddress = "10.0.0.2", UserAgent = "Chrome/125" },
    new() { UserId = "u3", Action = "purchase", Amount = 30.0, IpAddress = "10.0.0.3", UserAgent = "Safari/18" },
    new() { UserId = "u4", Action = "purchase", Amount = 89.99, IpAddress = "10.0.0.4", UserAgent = "Edge/130" },
};

Console.WriteLine("Filter: action == 'purchase' && amount > 50");
Console.WriteLine("Project: userId, action, amount (strip ipAddress, userAgent)\n");

foreach (var activity in activities)
{
    var result = pipeline.ProjectAsync(activity, default, CancellationToken.None).GetAwaiter().GetResult();
    if (result is null)
    {
        Console.WriteLine($"  {activity.UserId}: filtered out");
        continue;
    }

    Console.Write($"  {activity.UserId}: {{ ");
    for (int i = 0; i < result.Fields.Length; i++)
    {
        if (i > 0) Console.Write(", ");
        Console.Write($"{result.Fields[i].Name}={result.Fields[i].Value}");
    }
    Console.WriteLine(" }");
}

public sealed record UserActivity : IFilterSubject
{
    public string UserId { get; init; } = "";
    public string Action { get; init; } = "";
    public double Amount { get; init; }
    public string IpAddress { get; init; } = "";
    public string UserAgent { get; init; } = "";
}

public readonly struct EmptyContext;
