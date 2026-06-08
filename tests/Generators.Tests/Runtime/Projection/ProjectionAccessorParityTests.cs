using MessagePack;
using MessagePack.Resolvers;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionAccessorParityTests
{
    [Fact]
    public async Task ComposedFieldArrayUsesProjectionAccessor()
    {
        CompiledProjection<object?> projection = ProjectionCompiler.CompileWithSchema<object?>(
            typeof(AccessorProjectionEvent),
            EventProjectionExpression.Select(nameof(AccessorProjectionEvent.Value)),
            RejectInclude,
            ProjectionCompilerOptions.Immediate,
            errorFactory: null,
            _ => AccessorSchema(extraFields: 0));

        ProjectedEvent projected = await projection.ProjectAsync(
            new AccessorProjectionEvent(1, 2, 3, 4, 5),
            null,
            CancellationToken.None);

        Assert.Equal(999, projected.Field(nameof(AccessorProjectionEvent.Value)).Integer);
    }

    [Fact]
    public async Task PayloadWriterUsesProjectionAccessor()
    {
        string[] fields =
        [
            nameof(AccessorProjectionEvent.Value),
            nameof(AccessorProjectionEvent.Other1),
            nameof(AccessorProjectionEvent.Other2),
            nameof(AccessorProjectionEvent.Other3),
            nameof(AccessorProjectionEvent.Other4),
        ];
        CompiledProjection<object?> projection = ProjectionCompiler.CompileWithSchema<object?>(
            typeof(AccessorProjectionEvent),
            EventProjectionExpression.Select(fields),
            RejectInclude,
            ProjectionCompilerOptions.Immediate,
            errorFactory: null,
            _ => AccessorSchema(extraFields: 4));
        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

        ProjectedEvent materialized = await projection.ProjectAsync(
            new AccessorProjectionEvent(1, 2, 3, 4, 5),
            null,
            CancellationToken.None);
        ReadOnlyMemory<byte> payload = await projection.ProjectPayloadAsync(
            new AccessorProjectionEvent(1, 2, 3, 4, 5),
            null,
            options,
            CancellationToken.None);
        ProjectedEvent roundTripped = MessagePackSerializer.Deserialize<ProjectedEvent>(payload, options);

        Assert.Equal(999, materialized.Field(nameof(AccessorProjectionEvent.Value)).Integer);
        Assert.Equal(999, roundTripped.Field(nameof(AccessorProjectionEvent.Value)).Integer);
    }

    [Theory]
    [MemberData(nameof(ProjectionOptions))]
    public async Task HiddenPropertyProjectionUsesDerivedMember(ProjectionCompilerOptions options)
    {
        CompiledProjection<object?> projection = ProjectionCompiler.Compile<object?>(
            typeof(DerivedHiddenProjectionEvent),
            EventProjectionExpression.Select(nameof(DerivedHiddenProjectionEvent.Code)),
            RejectInclude,
            options);

        ProjectedEvent projected = await projection.ProjectAsync(
            new DerivedHiddenProjectionEvent(),
            null,
            CancellationToken.None);

        Assert.Equal(42, projected.Field(nameof(DerivedHiddenProjectionEvent.Code)).Integer);
    }

    public static TheoryData<ProjectionCompilerOptions> ProjectionOptions =>
    [
        ProjectionCompilerOptions.Immediate,
        ProjectionCompilerOptions.Tiered with
        {
            TieredPromotionMinimumAge = TimeSpan.FromDays(1),
            TieredPromotionMinimumOperations = int.MaxValue,
        },
    ];

    private static FilterSchema AccessorSchema(int extraFields)
    {
        var fields = new List<FilterField>
        {
            AccessorField(nameof(AccessorProjectionEvent.Value), projectedValue: 999),
        };
        if (extraFields >= 1) fields.Add(AccessorField(nameof(AccessorProjectionEvent.Other1), projectedValue: 2));
        if (extraFields >= 2) fields.Add(AccessorField(nameof(AccessorProjectionEvent.Other2), projectedValue: 3));
        if (extraFields >= 3) fields.Add(AccessorField(nameof(AccessorProjectionEvent.Other3), projectedValue: 4));
        if (extraFields >= 4) fields.Add(AccessorField(nameof(AccessorProjectionEvent.Other4), projectedValue: 5));
        return new FilterSchema(typeof(AccessorProjectionEvent), fields);
    }

    private static FilterField AccessorField(string name, long projectedValue) =>
        new(
            name,
            typeof(int),
            FilterFieldKind.Scalar,
            subject => typeof(AccessorProjectionEvent).GetProperty(name)!.GetValue(subject),
            ProjectionAccessor: _ => ProjectedEventValue.FromScalar(projectedValue),
            Access: FilterFieldAccess.ForProperty(name));

    private static CompiledProjection<object?>.IncludeProjector RejectInclude(
        FilterSchema schema,
        EventProjectionInclude include)
    {
        _ = schema;
        throw new InvalidOperationException($"Unexpected include '{include.Intrinsic}'.");
    }

    private sealed record AccessorProjectionEvent(
        int Value,
        int Other1,
        int Other2,
        int Other3,
        int Other4);

    private class BaseHiddenProjectionEvent
    {
        public string Code { get; } = "base";
    }

    private sealed class DerivedHiddenProjectionEvent : BaseHiddenProjectionEvent
    {
        public new int Code { get; } = 42;
    }
}
