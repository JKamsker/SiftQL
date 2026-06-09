using System.Reflection;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Kernel;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterRuntimeRegressionTests
{
    [Fact]
    public void PrecompiledFilterProviderCannotBypassValidation()
    {
        using var providerScope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(new AlwaysProvider());
        var invalid = FilterExpression.Compare(
            "MissingField",
            FilterOperator.Equal,
            FilterValue.From(1L));

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ItemUsedEvent), invalid, FilterCompilerOptions.Tiered));
    }

    [Fact]
    public void ParameterizedPrecompiledFilterProviderCannotBypassValidation()
    {
        using var providerScope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(new ParameterizedProvider());
        var invalid = FilterExpression.Compare(
            "MissingField",
            FilterOperator.Equal,
            FilterValue.From(1L) with { ParameterKey = "p0" });

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ItemUsedEvent), invalid, FilterCompilerOptions.Tiered));
    }

    [Fact]
    public void CustomSchemaCompileDoesNotPoisonDefaultSchemaCache()
    {
        string fieldName = "Synthetic" + Guid.NewGuid().ToString("N");
        var filter = FilterExpression.Compare(
            fieldName,
            FilterOperator.Equal,
            FilterValue.From(true));

        CompiledKernel synthetic = FilterCompiler.CompileWithSchema(
            typeof(ProjectedEvent),
            filter,
            FilterCompilerOptions.Immediate,
            errorFactory: null,
            _ => SyntheticSchema(fieldName));

        Assert.True(synthetic.Matches(new ProjectedEvent()));
        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ProjectedEvent), filter, FilterCompilerOptions.Immediate));
    }

    [Fact]
    public void RuntimeContainsShortCircuitsOversizedEnumerableAfterFirstMatch()
    {
        bool matched = FilterValues.Contains(
            OversizedEnumerable(first: 42, count: 257),
            FilterValue.From(42L));

        Assert.True(matched);
    }

    [Fact]
    public void NestedScalarFilterReturnsFalseWhenParentIsNull()
    {
        FilterSchema.RegisterValueObject<NestedLocation>();
        var filter = FilterExpression.Compare(
            "Location.Country",
            FilterOperator.Equal,
            FilterValue.From("AT"));

        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(NestedSubject),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.False(kernel.Matches(new NestedSubject(null!)));
        Assert.True(kernel.Matches(new NestedSubject(new NestedLocation("AT", 21))));
    }

    [Fact]
    public void NestedValueTypeExistsReturnsFalseWhenParentIsNull()
    {
        FilterSchema.RegisterValueObject<NestedLocation>();
        var filter = FilterExpression.Exists("Location.Temperature");

        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(NestedSubject),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.False(kernel.Matches(new NestedSubject(null!)));
        Assert.True(kernel.Matches(new NestedSubject(new NestedLocation("AT", 21))));
    }

    [Fact]
    public void NestedArrayContainsReturnsFalseWhenParentIsNull()
    {
        FilterSchema.RegisterValueObject<NestedArrayContainer>();
        var filter = FilterExpression.Contains(
            "Container.Tags",
            FilterValue.From("rare"));

        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(NestedArraySubject),
            filter,
            FilterCompilerOptions.Immediate);

        Assert.False(kernel.Matches(new NestedArraySubject(null!)));
        Assert.False(kernel.Matches(new NestedArraySubject(new NestedArrayContainer(["common"]))));
        Assert.True(kernel.Matches(new NestedArraySubject(new NestedArrayContainer(["rare"]))));
    }

    [Fact]
    public void ImmediateCompiledMatcherDoesNotTrackKernelVersionForever()
    {
        CompiledKernel kernel = FilterCompiler.Compile(
            typeof(ItemUsedEvent),
            FilterExpression.Compare(
                nameof(ItemUsedEvent.ItemId),
                FilterOperator.Equal,
                FilterValue.From(100L)),
            FilterCompilerOptions.Immediate);

        var matcher = kernel.CreateMatcher<ItemUsedEvent>();
        FieldInfo trackVersion = typeof(CompiledKernelMatcher<ItemUsedEvent>).GetField(
            "_trackVersion",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.False((bool)trackVersion.GetValue(matcher)!);
    }

    [Fact]
    public void HotProviderRegistrationScopeDoesNotPublishBeforeCommit()
    {
        using var providerScope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var manifestScope = HotProviderRegistrationContext.AllowManifest("manifest-hash");

        HotProviderRegistrationContext.Register(new AlwaysProvider(), "manifest-hash");

        Assert.False(PrecompiledTieredProviderRegistry.TryGetFilter(
            typeof(ItemUsedEvent),
            "fingerprint",
            out _));

        manifestScope.Commit();

        Assert.False(PrecompiledTieredProviderRegistry.TryGetFilter(
            typeof(ItemUsedEvent),
            "fingerprint",
            out _));

        using var registration = manifestScope.ClaimCommittedRegistrations();

        Assert.True(PrecompiledTieredProviderRegistry.TryGetFilter(
            typeof(ItemUsedEvent),
            "fingerprint",
            out _));
    }

    [Fact]
    public void OversizedFilterValidatesBeforeHotProviderLookup()
    {
        using var providerScope = PrecompiledTieredProviderRegistry.CreateIsolatedScope();
        using var registration = PrecompiledTieredProviderRegistry.Register(new ThrowingProvider());

        var oversized = new FilterExpression(FilterExpressionKind.And)
        {
            Children = Enumerable.Range(0, 129)
                .Select(static item => FilterExpression.Compare(
                    nameof(ItemUsedEvent.ItemId),
                    FilterOperator.Equal,
                    FilterValue.From(item)))
                .ToArray(),
        };

        Assert.Throws<FilterValidationException>(() =>
            FilterCompiler.Compile(typeof(ItemUsedEvent), oversized, FilterCompilerOptions.Tiered));
    }

    private static FilterSchema SyntheticSchema(string fieldName) =>
        new(
            typeof(ProjectedEvent),
            [
                new FilterField(
                    fieldName,
                    typeof(bool),
                    FilterFieldKind.Scalar,
                    static _ => true,
                    ProjectionAccessor: static _ => ProjectedEventValue.FromScalar(true)),
            ]);

    private static IEnumerable<int> OversizedEnumerable(int first, int count)
    {
        yield return first;
        for (int i = 1; i < count; i++)
            yield return i;
    }

    private sealed record NestedSubject(NestedLocation Location) : IFilterSubject;

    private sealed record NestedLocation(string Country, int Temperature);

    private sealed record NestedArraySubject(NestedArrayContainer Container) : IFilterSubject;

    private sealed record NestedArrayContainer(string[] Tags);

    private sealed class AlwaysProvider : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? predicate)
        {
            _ = subjectType;
            _ = fingerprint;
            predicate = static _ => true;
            return true;
        }

        public bool TryGetProjection(
            Type subjectType,
            string fingerprint,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            _ = subjectType;
            _ = fingerprint;
            projectFields = null;
            return false;
        }
    }

    private sealed class ThrowingProvider : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? predicate)
        {
            _ = subjectType;
            _ = fingerprint;
            predicate = null;
            throw new InvalidOperationException("Provider lookup happened before validation.");
        }

        public bool TryGetProjection(
            Type subjectType,
            string fingerprint,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            _ = subjectType;
            _ = fingerprint;
            projectFields = null;
            return false;
        }
    }

    private sealed class ParameterizedProvider : IPrecompiledTieredProvider
    {
        public bool TryGetFilter(Type subjectType, string fingerprint, out Func<object, bool>? predicate)
        {
            _ = subjectType;
            _ = fingerprint;
            predicate = null;
            return false;
        }

        public bool TryGetParameterizedFilter(
            Type subjectType,
            string fingerprint,
            out ParameterizedHotFilterPredicate? predicate)
        {
            _ = subjectType;
            _ = fingerprint;
            predicate = static (_, _) => true;
            return true;
        }

        public bool TryGetProjection(
            Type subjectType,
            string fingerprint,
            out Func<object, ProjectedEventField[]>? projectFields)
        {
            _ = subjectType;
            _ = fingerprint;
            projectFields = null;
            return false;
        }
    }
}
