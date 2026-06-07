using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratorHarnessFact
{
    [Fact]
    public void RunFilterSchemaHarness()
    {
        FilterSchemaSourceGeneratorTests.RunAll();
        FilterSchemaParitySourceGeneratorTests.RunAll();
        HotManifestLiteralValidationTests.RunAll();
        HotProviderNullStringLiteralTests.RunAll();
        HotManifestProjectionValidationTests.RunAll();
        HotManifestPathStabilityTests.RunAll();
        HotManifestShapeValidationTests.RunAll();
        HotProviderFingerprintValidationTests.RunAll();
        HotProviderLoaderLifecycleTests.RunAll();
        HotProviderSourceGeneratorTests.RunAll();
        HotProviderSemanticValidationTests.RunAll();
        HotProviderValidationTests.RunAll();
        KeywordFilterSchemaSourceGeneratorTests.RunAll();
        ParameterizedHotProviderSourceGeneratorTests.RunAll();
        ProjectedHotProviderSourceGeneratorTests.RunAll();
    }

    [Fact]
    public void RunFilterSchemaHarnessInvokesEveryStandaloneSuite()
    {
        string harnessSource = File.ReadAllText(CurrentFilePath());
        string[] suiteNames = typeof(GeneratorHarnessFact).Assembly
            .GetTypes()
            .Where(static type =>
                type.Namespace == typeof(GeneratorHarnessFact).Namespace &&
                type.IsAbstract &&
                type.IsSealed &&
                type.Name.EndsWith("Tests", StringComparison.Ordinal) &&
                HasParameterlessRunAll(type))
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (string suiteName in suiteNames)
            Assert.Contains($"{suiteName}.RunAll()", harnessSource);
    }

    private static bool HasParameterlessRunAll(Type type) =>
        type.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static) is { } method &&
        method.GetParameters().Length == 0;

    private static string CurrentFilePath([CallerFilePath] string path = "") => path;
}
