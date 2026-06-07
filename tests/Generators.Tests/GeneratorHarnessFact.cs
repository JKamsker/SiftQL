using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratorHarnessFact
{
    [Fact]
    public void RunFilterSchemaHarness()
    {
        EventPipelineRegressionTests.RunAll();
        FilterSchemaSourceGeneratorTests.RunAll();
        FilterSchemaGeneratorRegressionTests.RunAll();
        FilterSchemaFallbackRegressionTests.RunAll();
        FilterSchemaParitySourceGeneratorTests.RunAll();
        FilterCompilerCacheRegressionTests.RunAll();
        FilterNumericPrecisionRegressionTests.RunAll();
        FilterRuntimeRegressionTests.RunAll();
        HotCompilationManifestWriterRegressionTests.RunAll();
        HotManifestLiteralValidationTests.RunAll();
        HotProviderNullStringLiteralTests.RunAll();
        HotManifestProjectionValidationTests.RunAll();
        HotManifestPathStabilityTests.RunAll();
        HotManifestIdentityRegressionTests.RunAll();
        HotManifestShapeValidationTests.RunAll();
        HotProviderFingerprintValidationTests.RunAll();
        HotProviderDuplicateFingerprintTests.RunAll();
        HotProviderLoaderLifecycleTests.RunAll();
        HotProviderLoaderManifestIsolationTests.RunAll();
        HotProviderRegistrationGateTests.RunAll();
        HotProviderSourceGeneratorTests.RunAll();
        HotProviderSemanticValidationTests.RunAll();
        HotProviderValidationTests.RunAll();
        HotProviderValueCompatibilityTests.RunAll();
        KernelCatalogSourceGeneratorTests.RunAll();
        KeywordFilterSchemaSourceGeneratorTests.RunAll();
        ParameterizedHotProviderSourceGeneratorTests.RunAll();
        ProjectionCompilerRuntimeTests.RunAll();
        ProjectionDecimalRegressionTests.RunAll();
        ProjectionPayloadWriterRegressionTests.RunAll();
        ProjectedHotProviderSourceGeneratorTests.RunAll();
        QueryKernelProjectionRegressionTests.RunAll();
        RuntimeHotProviderBatchSinkTests.RunAll();
        ServerPluginHostExampleTests.RunAll();
        TieredProjectionRegressionTests.RunAll();
        TieredProviderRecoveryTests.RunAll();
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
