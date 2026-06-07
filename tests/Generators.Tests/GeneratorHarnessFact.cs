using System.Reflection;
using System.Runtime.ExceptionServices;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratorHarnessFact
{
    private const string RunAllMethodName = "RunAll";

    public static IEnumerable<object[]> StandaloneSuites =>
        typeof(GeneratorHarnessFact).Assembly
            .GetTypes()
            .Where(static type =>
                type.Namespace == typeof(GeneratorHarnessFact).Namespace &&
                type.IsAbstract &&
                type.IsSealed &&
                type.Name.EndsWith("Tests", StringComparison.Ordinal) &&
                HasParameterlessRunAll(type))
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .Select(static type => new object[] { type.FullName! });

    [Theory]
    [MemberData(nameof(StandaloneSuites))]
    public void RunStandaloneSuite(string typeName)
    {
        Type type = typeof(GeneratorHarnessFact).Assembly.GetType(typeName, throwOnError: true)!;
        MethodInfo runAll = type.GetMethod(RunAllMethodName, BindingFlags.Public | BindingFlags.Static)!;

        Exception? exception = Record.Exception(() => runAll.Invoke(null, null));

        if (exception is TargetInvocationException { InnerException: { } inner })
            exception = inner;

        if (exception is not null)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private static bool HasParameterlessRunAll(Type type) =>
        type.GetMethod(RunAllMethodName, BindingFlags.Public | BindingFlags.Static) is { } method &&
        method.GetParameters().Length == 0;
}
