using System.Collections.Immutable;
using System.Text.Json;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace SiftQL.Generators.Tests;

public sealed class HotProviderRegistrationFactoryRegressionTests
{
    [Fact]
    public void FactoryRegistrationWaitsForModuleInitializerSchemaRegistration()
    {
        const string assemblyName = "Plugin.Hot.FactoryRegistration";
        FilterExpression filter = FilterExpression.Compare(
            "Location.Code",
            FilterOperator.Equal,
            FilterValue.From("north"));
        string fingerprint = FilterExpressionFingerprint.Create(filter);
        string manifestJson = ManifestJson(filter, assemblyName, fingerprint);
        string manifestHash = HotManifestSemanticHash.Compute(manifestJson);
        SyntaxTree source = CSharpSyntaxTree.ParseText(
            ProviderSource(manifestHash, fingerprint),
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, source);
        string directory = Path.Combine(
            Path.GetTempPath(),
            "SiftQLHotFactoryRegistration",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string assemblyPath = Path.Combine(directory, assemblyName + ".dll");
        string manifestPath = Path.Combine(directory, "hot.json");
        EmitResult emit = compilation.Emit(assemblyPath);
        AssertEx.True(emit.Success, "factory hot provider emitted: " + string.Join(" | ", emit.Diagnostics));
        File.WriteAllText(manifestPath, manifestJson);

        using var scope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using HotTieredProviderLoadResult result = HotTieredProviderLoader.TryLoad(new()
        {
            AssemblyPath = assemblyPath,
            ManifestPath = manifestPath,
            RequireExactRuntimeVersion = false,
        });

        AssertEx.True(result.Loaded, "factory hot provider loaded: " + result.Message);
        Type subjectType = result.Assembly!.GetType("Plugin.Events.FactoryEvent", throwOnError: true)!;
        object matching = Event(subjectType, "north");
        object nonMatching = Event(subjectType, "south");
        CompiledKernel kernel = FilterCompiler.Compile(subjectType, filter, FilterCompilerOptions.Tiered);

        AssertEx.True(!kernel.IsTiered, "factory provider registered after schema registration");
        Assert.True(kernel.Matches(matching));
        Assert.False(kernel.Matches(nonMatching));
    }

    private static object Event(Type subjectType, string code)
    {
        Type locationType = subjectType.Assembly.GetType("Plugin.Events.Location", throwOnError: true)!;
        object location = Activator.CreateInstance(locationType, code)!;
        return Activator.CreateInstance(subjectType, Guid.NewGuid(), location)!;
    }

    private static string ManifestJson(
        FilterExpression filter,
        string assemblyName,
        string fingerprint) =>
        JsonSerializer.Serialize(new HotCompilationManifest
        {
            RuntimeVersion = "10.0.0",
            Entries =
            [
                new HotCompilationManifestEntry
                {
                    Key = "filter|Plugin.Events.FactoryEvent, " + assemblyName + "|" + fingerprint,
                    Kind = "filter",
                    SubjectType = "Plugin.Events.FactoryEvent, " + assemblyName,
                    Fingerprint = fingerprint,
                    Definition = JsonSerializer.SerializeToElement(filter),
                },
            ],
        });

    private static string ProviderSource(string manifestHash, string fingerprint) =>
        $$"""
        using System;
        using System.Reflection;
        using System.Runtime.CompilerServices;
        using SiftQL;
        using SiftQL.Hot;
        using SiftQL.Projected;
        using SiftQL.Schema;

        [assembly: AssemblyMetadata("SiftQLHotManifestHash", "{{manifestHash}}")]
        [assembly: AssemblyMetadata("SiftQLHotManifestSchema", "siftql.hot.v1")]
        [assembly: AssemblyMetadata("SiftQLHotFilterEngine", "tiered-v1")]
        [assembly: AssemblyMetadata("SiftQLHotGenerator", "hot-sourcegen-v1")]

        namespace Plugin.Events;

        public sealed record Location(string Code);

        public sealed record FactoryEvent(Guid EventId, Location Location) : IFilterSubject;

        internal static class FactoryRegistration
        {
            [ModuleInitializer]
            public static void Initialize()
            {
                HotProviderRegistrationContext.RegisterFactory(static () => new FactoryProvider(), "{{manifestHash}}");
                GeneratedFilterSchemaRegistry.Register(typeof(FactoryEvent).Assembly, TryCreate);
            }

            private static bool TryCreate(Type subjectType, out FilterSchema? schema)
            {
                if (subjectType != typeof(FactoryEvent))
                {
                    schema = null;
                    return false;
                }

                schema = GeneratedFilterSchemaRegistry.Create(
                    subjectType,
                    [
                        new FilterField(
                            "Location.Code",
                            typeof(string),
                            FilterFieldKind.Scalar,
                            static subject => ((FactoryEvent)subject).Location.Code,
                            new FilterScalarAccessor(
                                FilterScalarKind.String,
                                text: static subject => ((FactoryEvent)subject).Location.Code),
                            ProjectionAccessor: static subject => ProjectedEventValue.FromScalar(((FactoryEvent)subject).Location.Code),
                            Access: FilterFieldAccess.ForProperty("Location.Code")),
                    ]);
                return true;
            }
        }

        internal sealed class FactoryProvider : IPrecompiledTieredProvider
        {
            public FactoryProvider()
            {
                if (!FilterSchema.For(typeof(FactoryEvent)).TryGetField("Location.Code", out _))
                    throw new InvalidOperationException("Generated schema is not available yet.");
            }

            public bool TryGetFilter(
                Type subjectType,
                string fingerprint,
                out Func<object, bool>? predicate)
            {
                if (subjectType == typeof(FactoryEvent) && fingerprint == "{{fingerprint}}")
                {
                    predicate = static subject => ((FactoryEvent)subject).Location.Code == "north";
                    return true;
                }

                predicate = null;
                return false;
            }

            public bool TryGetProjection(
                Type subjectType,
                string fingerprint,
                out Func<object, ProjectedEventField[]>? projectFields)
            {
                projectFields = null;
                return false;
            }
        }
        """;
}
