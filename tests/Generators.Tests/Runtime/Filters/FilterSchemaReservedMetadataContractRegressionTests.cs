using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using SiftQL.Compiler;
using SiftQL.Generators;
using SiftQL.Generators.Schema;
using SiftQL.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaReservedMetadataContractRegressionTests
{
    [Fact]
    public void InterfaceProviderCannotSpoofReservedMetadataWithoutProbe()
    {
        Type subjectType = CreateInterfaceSubject();
        GeneratedFilterSchemaRegistry.Register(subjectType.Assembly, Provider);

        Assert.Throws<FilterValidationException>(() => FilterSchema.For(subjectType));

        bool Provider(Type candidate, out FilterSchema? schema)
        {
            if (candidate != subjectType)
            {
                schema = null;
                return false;
            }

            schema = GeneratedFilterSchemaRegistry.Create(
                candidate,
                [
                    SpoofedReserved("subjectType"),
                    SpoofedReserved("subjectName"),
                ]);
            return true;
        }
    }

    [Fact]
    public void GeneratedUnsealedSubjectWithoutDerivableConstructorKeepsReservedMetadataValid()
    {
        GeneratorRun run = RunGenerator(
            "Plugin.Schema.PrivateCtor",
            Source("""
                using SiftQL;

                namespace Plugin.Events;

                public class PrivateCtorEvent : IFilterSubject
                {
                    private PrivateCtorEvent() { }

                    public int Id { get; private set; }

                    public static PrivateCtorEvent Create(int id) => new PrivateCtorEvent { Id = id };
                }
                """));
        AssertNoCompilationErrors(run.OutputCompilation);
        Assembly assembly = EmitAndLoad(run.OutputCompilation);
        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
        Type eventType = assembly.GetType("Plugin.Events.PrivateCtorEvent", throwOnError: true)!;

        FilterSchema schema = FilterSchema.For(eventType);

        Assert.True(schema.TryGetField("subjectType", out _));
        Assert.True(schema.TryGetField("Id", out _));
    }

    private static FilterField SpoofedReserved(string name) =>
        new(
            name,
            typeof(string),
            FilterFieldKind.Scalar,
            static _ => "spoofed",
            new FilterScalarAccessor(FilterScalarKind.String, text: static _ => "spoofed"));

    private static Type CreateInterfaceSubject()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("SiftQL.ReservedInterface." + Guid.NewGuid().ToString("N")),
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("main");
        TypeBuilder type = module.DefineType(
            "Plugin.Events.IReservedInterface",
            TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        type.AddInterfaceImplementation(typeof(IFilterSubject));
        return type.CreateType()!;
    }

    private static GeneratorRun RunGenerator(string assemblyName, SyntaxTree source)
    {
        CSharpCompilation compilation = GeneratorTestCompilation.Create(assemblyName, source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: ImmutableArray.Create<ISourceGenerator>(new FilterSchemaSourceGenerator().AsSourceGenerator()),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out Compilation output,
            out ImmutableArray<Diagnostic> diagnostics);
        Assert.Empty(diagnostics);
        return new(output);
    }

    private static SyntaxTree Source(string source) =>
        CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static Assembly EmitAndLoad(Compilation output)
    {
        using var pe = new MemoryStream();
        var emit = output.Emit(pe);
        Assert.True(emit.Success, string.Join(" | ", emit.Diagnostics));
        return Assembly.Load(pe.ToArray());
    }

    private static void AssertNoCompilationErrors(Compilation output)
    {
        Diagnostic[] errors = output.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.Empty(errors);
    }

    private sealed record GeneratorRun(Compilation OutputCompilation);
}
