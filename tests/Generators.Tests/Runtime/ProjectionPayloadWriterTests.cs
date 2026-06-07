using MessagePack;
using MessagePack.Resolvers;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionPayloadWriterTests
{
    private static MessagePackSerializerOptions PayloadOptions { get; } =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private sealed record PayloadSubject(
        bool Flag = false,
        bool? NullableFlag = null,
        int IntVal = 0,
        int? NullableInt = null,
        long LongVal = 0L,
        ulong ULongVal = 0UL,
        float FloatVal = 0f,
        double DoubleVal = 0.0,
        decimal DecimalVal = 0m,
        string? StringVal = null,
        Guid GuidVal = default) : IFilterSubject;

    private static CompiledProjection<object> CompilePayloadProjection(params string[] fields) =>
        ProjectionCompiler.Compile<object>(
            typeof(PayloadSubject),
            EventProjectionExpression.Select(fields),
            static (_, inc) => throw new InvalidOperationException());

    private static async Task<ProjectedEvent> RoundTripAsync(CompiledProjection<object> proj, PayloadSubject subject)
    {
        ReadOnlyMemory<byte> payload = await proj.ProjectPayloadAsync(
            subject, new object(), PayloadOptions, CancellationToken.None);
        return MessagePackSerializer.Deserialize<ProjectedEvent>(payload, PayloadOptions);
    }

    [Fact]
    public async Task PayloadWriter_BoolTrue_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.Flag));
        var result = await RoundTripAsync(proj, new PayloadSubject(Flag: true));
        Assert.True(result.TryGetField(nameof(PayloadSubject.Flag), out var val));
        Assert.Equal(ProjectedEventValueKind.Boolean, val.Kind);
        Assert.True(val.Boolean);
    }

    [Fact]
    public async Task PayloadWriter_NullableBool_Null_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableFlag));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableFlag: null));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableFlag), out var val));
        Assert.Equal(ProjectedEventValueKind.Null, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_NullableBool_HasValue_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableFlag));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableFlag: false));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableFlag), out var val));
        Assert.Equal(ProjectedEventValueKind.Boolean, val.Kind);
        Assert.False(val.Boolean);
    }

    [Fact]
    public async Task PayloadWriter_IntField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.IntVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(IntVal: 42));
        Assert.True(result.TryGetField(nameof(PayloadSubject.IntVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(42L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_NullableInt_Null_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableInt));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableInt: null));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableInt), out var val));
        Assert.Equal(ProjectedEventValueKind.Null, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_NullableInt_HasValue_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.NullableInt));
        var result = await RoundTripAsync(proj, new PayloadSubject(NullableInt: 99));
        Assert.True(result.TryGetField(nameof(PayloadSubject.NullableInt), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(99L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_LongField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.LongVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(LongVal: 9999L));
        Assert.True(result.TryGetField(nameof(PayloadSubject.LongVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(9999L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_FloatField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.FloatVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(FloatVal: 1.5f));
        Assert.True(result.TryGetField(nameof(PayloadSubject.FloatVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Number, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_DoubleField_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.DoubleVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(DoubleVal: 2.71));
        Assert.True(result.TryGetField(nameof(PayloadSubject.DoubleVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Number, val.Kind);
        Assert.Equal(2.71, val.Number, 5);
    }

    [Fact]
    public async Task PayloadWriter_DecimalIntegral_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.DecimalVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(DecimalVal: 10m));
        Assert.True(result.TryGetField(nameof(PayloadSubject.DecimalVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Integer, val.Kind);
        Assert.Equal(10L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_DecimalFractional_Written()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.DecimalVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(DecimalVal: 1.5m));
        Assert.True(result.TryGetField(nameof(PayloadSubject.DecimalVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Decimal, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_ULongSmall_WrittenAsInteger()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.ULongVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(ULongVal: 100UL));
        Assert.True(result.TryGetField(nameof(PayloadSubject.ULongVal), out var val));
        Assert.Equal(100L, val.Integer);
    }

    [Fact]
    public async Task PayloadWriter_ULongBig_WrittenAsUnsignedInteger()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var proj = CompilePayloadProjection(nameof(PayloadSubject.ULongVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(ULongVal: big));
        Assert.True(result.TryGetField(nameof(PayloadSubject.ULongVal), out var val));
        Assert.Equal(ProjectedEventValueKind.UnsignedInteger, val.Kind);
        Assert.Equal(big, val.UnsignedInteger);
    }

    [Fact]
    public async Task PayloadWriter_StringNonNull_WrittenAsString()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.StringVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(StringVal: "hello"));
        Assert.True(result.TryGetField(nameof(PayloadSubject.StringVal), out var val));
        Assert.Equal("hello", val.String);
    }

    [Fact]
    public async Task PayloadWriter_StringNull_WrittenAsNull()
    {
        var proj = CompilePayloadProjection(nameof(PayloadSubject.StringVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(StringVal: null));
        Assert.True(result.TryGetField(nameof(PayloadSubject.StringVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Null, val.Kind);
    }

    [Fact]
    public async Task PayloadWriter_GuidField_WrittenAsGuid()
    {
        var g = Guid.NewGuid();
        var proj = CompilePayloadProjection(nameof(PayloadSubject.GuidVal));
        var result = await RoundTripAsync(proj, new PayloadSubject(GuidVal: g));
        Assert.True(result.TryGetField(nameof(PayloadSubject.GuidVal), out var val));
        Assert.Equal(ProjectedEventValueKind.Guid, val.Kind);
        Assert.Equal(g, val.Guid);
    }
}
