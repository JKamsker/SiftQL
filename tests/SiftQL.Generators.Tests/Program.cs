using SiftQL.Generators.Tests;

FilterSchemaSourceGeneratorTests.RunAll();
FilterSchemaParitySourceGeneratorTests.RunAll();
HotProviderNullStringLiteralTests.RunAll();
HotManifestProjectionValidationTests.RunAll();
HotManifestPathStabilityTests.RunAll();
HotProviderFingerprintValidationTests.RunAll();
HotProviderSourceGeneratorTests.RunAll();
HotProviderLoaderLifecycleTests.RunAll();
HotProviderSemanticValidationTests.RunAll();
HotProviderValidationTests.RunAll();
ParameterizedHotProviderSourceGeneratorTests.RunAll();
ProjectedHotProviderSourceGeneratorTests.RunAll();
Console.WriteLine("Filter schema generator tests OK");
