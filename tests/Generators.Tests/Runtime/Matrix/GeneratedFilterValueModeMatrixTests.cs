using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedFilterValueModeMatrixTests
{
    private const string EventTypeName = "Plugin.Events.ValueEvent";

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void OrderedComparisonToNullThrowsValidationException(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ValueNullOrder." + mode;
        FilterExpression filter = FilterExpression.Compare(
            "Score",
            FilterOperator.GreaterThan,
            FilterValue.Null);
        // Hot manifest validation rejects this entry before runtime compilation.
        GeneratedExecutionMode contextMode = mode == GeneratedExecutionMode.GeneratedHot
            ? GeneratedExecutionMode.Interpreted
            : mode;
        using var context = LoadContext(contextMode, assemblyName);

        Assert.Throws<FilterValidationException>(() => FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode)));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void StringKindNullDoesNotMatchNullString(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ValueStringNull." + mode;
        FilterValue stringNull = new() { Kind = FilterValueKind.String, String = null };
        FilterExpression compare = FilterExpression.Compare(
            "Name",
            FilterOperator.Equal,
            stringNull);
        FilterExpression inFilter = FilterExpression.In("Name", [stringNull]);
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), compare),
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), inFilter));

        CompiledKernel compareKernel = FilterCompiler.Compile(
            context.EventType,
            compare,
            GeneratedModeMatrixSupport.FilterOptions(mode));
        CompiledKernel inKernel = FilterCompiler.Compile(
            context.EventType,
            inFilter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.False(compareKernel.Matches(Event(context.EventType, score: 1.0, name: null)));
        Assert.False(compareKernel.Matches(Event(context.EventType, score: 1.0, name: "alpha")));
        Assert.False(inKernel.Matches(Event(context.EventType, score: 1.0, name: null)));
        Assert.False(inKernel.Matches(Event(context.EventType, score: 1.0, name: "alpha")));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void StringKindNullContainsDoesNotMatchNullArrayElement(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ValueStringNullContains." + mode;
        FilterValue stringNull = new() { Kind = FilterValueKind.String, String = null };
        FilterExpression filter = FilterExpression.Contains("Tags", stringNull);
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), filter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.False(kernel.Matches(Event(context.EventType, score: 1.0, name: "alpha", tags: [null])));
        Assert.False(kernel.Matches(Event(context.EventType, score: 1.0, name: "alpha", tags: ["alpha"])));
    }

    [Theory]
    [MemberData(nameof(GeneratedModeMatrixSupport.Modes), MemberType = typeof(GeneratedModeMatrixSupport))]
    public void DecimalLiteralInDoesNotRoundToDoubleMatch(GeneratedExecutionMode mode)
    {
        string assemblyName = "Plugin.Matrix.ValueDecimalIn." + mode;
        FilterValue value = FilterValue.From(1.0000000000000000000000000001m);
        FilterExpression filter = FilterExpression.In("Score", [value]);
        using var context = LoadContext(
            mode,
            assemblyName,
            GeneratedModeMatrixSupport.FilterEntry(Subject(assemblyName), filter));

        CompiledKernel kernel = FilterCompiler.Compile(
            context.EventType,
            filter,
            GeneratedModeMatrixSupport.FilterOptions(mode));

        Assert.False(kernel.Matches(Event(context.EventType, score: 1.0, name: "alpha")));
    }

    private static GeneratedModeContext LoadContext(
        GeneratedExecutionMode mode,
        string assemblyName,
        params HotCompilationManifestEntry[] entries) =>
        GeneratedModeMatrixSupport.LoadContext(
            mode,
            assemblyName,
            EventTree(),
            EventTypeName,
            "generated filter value matrix",
            entries);

    private static SyntaxTree EventTree() =>
        CSharpSyntaxTree.ParseText("""
            #nullable enable
            using System;
            using SiftQL;

            namespace Plugin.Events;

            public sealed record ValueEvent(
                Guid EventId,
                double? Score,
                string? Name,
                string?[] Tags) : IFilterSubject;
            """, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

    private static string Subject(string assemblyName) =>
        GeneratedModeMatrixSupport.Subject(EventTypeName, assemblyName);

    private static object Event(Type eventType, double? score, string? name, string?[]? tags = null) =>
        Activator.CreateInstance(eventType, Guid.NewGuid(), score, name, tags ?? [])!;
}
