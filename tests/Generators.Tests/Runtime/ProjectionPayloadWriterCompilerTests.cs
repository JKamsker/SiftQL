using MessagePack;
using MessagePack.Resolvers;
using SiftQL.Compiler;
using SiftQL.Projected;
using SiftQL.Projection;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionPayloadWriterCompilerTests
{
    [Fact]
    public void TryCompile_ReturnsNull_ForNonScalarField()
    {
        var field = new FilterField(
            "Items", typeof(int[]), FilterFieldKind.Array,
            _ => null, ArrayAccessor: null);

        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Items", field);

        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_ReturnsNull_WhenAccessIsNull()
    {
        var field = new FilterField(
            "Name", typeof(string), FilterFieldKind.Scalar,
            _ => null);

        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Name", field);

        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_ReturnsNull_WhenPropertyNotFound()
    {
        var field = new FilterField(
            "Missing", typeof(string), FilterFieldKind.Scalar,
            _ => null, Access: FilterFieldAccess.ForProperty("NonexistentProperty"));

        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Missing", field);

        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_Boolean_Required()
    {
        var field = ScalarField("IsActive", typeof(bool), "IsActive");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "IsActive", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject(), "IsActive", v => Assert.True(v.Boolean));
    }

    [Fact]
    public void TryCompile_Boolean_Nullable()
    {
        var field = ScalarField("NullableBool", typeof(bool?), "NullableBool");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableBool", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableBool = true }, "NullableBool",
            v => Assert.True(v.Boolean));
        AssertPayloadField(writer!, new PayloadSubject { NullableBool = null }, "NullableBool",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Integer_Required()
    {
        var field = ScalarField("Count", typeof(int), "Count");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Count", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Count = 42 }, "Count",
            v => Assert.Equal(42, v.Integer));
    }

    [Fact]
    public void TryCompile_Integer_Nullable()
    {
        var field = ScalarField("NullableCount", typeof(int?), "NullableCount");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableCount", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableCount = 7 }, "NullableCount",
            v => Assert.Equal(7, v.Integer));
        AssertPayloadField(writer!, new PayloadSubject { NullableCount = null }, "NullableCount",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_UnsignedInteger_Required()
    {
        var field = ScalarField("BigId", typeof(ulong), "BigId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "BigId", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { BigId = (ulong)long.MaxValue + 1 }, "BigId",
            v => Assert.Equal((ulong)long.MaxValue + 1, v.UnsignedInteger));
    }

    [Fact]
    public void TryCompile_UnsignedInteger_Nullable()
    {
        var field = ScalarField("NullableBigId", typeof(ulong?), "NullableBigId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableBigId", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableBigId = 100UL }, "NullableBigId",
            v => Assert.Equal(100, v.Integer));
        AssertPayloadField(writer!, new PayloadSubject { NullableBigId = null }, "NullableBigId",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Double_Required()
    {
        var field = ScalarField("Score", typeof(double), "Score");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Score", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Score = 3.14 }, "Score",
            v => Assert.Equal(3.14, v.Number));
    }

    [Fact]
    public void TryCompile_Double_Nullable()
    {
        var field = ScalarField("NullableScore", typeof(double?), "NullableScore");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableScore", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullableScore = 2.71 }, "NullableScore",
            v => Assert.Equal(2.71, v.Number));
        AssertPayloadField(writer!, new PayloadSubject { NullableScore = null }, "NullableScore",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Float_Required()
    {
        var field = ScalarField("Ratio", typeof(float), "Ratio");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Ratio", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Ratio = 1.5f }, "Ratio",
            v => Assert.Equal(1.5, v.Number, precision: 5));
    }

    [Fact]
    public void TryCompile_Decimal_Required()
    {
        var field = ScalarField("Price", typeof(decimal), "Price");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Price", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Price = 99.99m }, "Price",
            v => Assert.Equal(99.99m, v.Decimal));
    }

    [Fact]
    public void TryCompile_Decimal_Nullable()
    {
        var field = ScalarField("NullablePrice", typeof(decimal?), "NullablePrice");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullablePrice", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { NullablePrice = 10m }, "NullablePrice",
            v => Assert.Equal(10, v.Integer));
        AssertPayloadField(writer!, new PayloadSubject { NullablePrice = null }, "NullablePrice",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Decimal_WholeNumber_WrittenAsInteger()
    {
        var field = ScalarField("Price", typeof(decimal), "Price");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Price", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Price = 42m }, "Price",
            v => Assert.Equal(42, v.Integer));
    }

    [Fact]
    public void TryCompile_String()
    {
        var field = ScalarField("Name", typeof(string), "Name");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Name", field);
        Assert.NotNull(writer);

        AssertPayloadField(writer!, new PayloadSubject { Name = "hello" }, "Name",
            v => Assert.Equal("hello", v.String));
        AssertPayloadField(writer!, new PayloadSubject { Name = null }, "Name",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_Guid_Required()
    {
        var field = ScalarField("Id", typeof(Guid), "Id");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Id", field);
        Assert.NotNull(writer);
        var guid = Guid.NewGuid();
        AssertPayloadField(writer!, new PayloadSubject { Id = guid }, "Id",
            v => Assert.Equal(guid, v.Guid));
    }

    [Fact]
    public void TryCompile_Guid_Nullable()
    {
        var field = ScalarField("NullableId", typeof(Guid?), "NullableId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NullableId", field);
        Assert.NotNull(writer);

        var guid = Guid.NewGuid();
        AssertPayloadField(writer!, new PayloadSubject { NullableId = guid }, "NullableId",
            v => Assert.Equal(guid, v.Guid));
        AssertPayloadField(writer!, new PayloadSubject { NullableId = null }, "NullableId",
            v => Assert.Equal(ProjectedEventValueKind.Null, v.Kind));
    }

    [Fact]
    public void TryCompile_NestedProperty()
    {
        var field = ScalarField("Nested.Value", typeof(int), "Nested.Value");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "NestedValue", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Nested = new NestedObj { Value = 77 } },
            "NestedValue", v => Assert.Equal(77, v.Integer));
    }

    [Fact]
    public void TryCompile_UnsupportedType_ReturnsNull()
    {
        var field = ScalarField("Created", typeof(DateTime), "Created");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Created", field);
        Assert.Null(writer);
    }

    [Fact]
    public void TryCompile_ByteProperty_HandledAsSignedInteger()
    {
        var field = ScalarField("Level", typeof(byte), "Level");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "Level", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { Level = 200 }, "Level",
            v => Assert.Equal(200, v.Integer));
    }

    [Fact]
    public void TryCompile_ShortProperty_HandledAsSignedInteger()
    {
        var field = ScalarField("ShortVal", typeof(short), "ShortVal");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "ShortVal", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { ShortVal = -100 }, "ShortVal",
            v => Assert.Equal(-100, v.Integer));
    }

    [Fact]
    public void TryCompile_LongProperty_HandledAsSignedInteger()
    {
        var field = ScalarField("LongVal", typeof(long), "LongVal");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "LongVal", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { LongVal = long.MaxValue }, "LongVal",
            v => Assert.Equal(long.MaxValue, v.Integer));
    }

    [Fact]
    public void TryCompile_UInt64_SmallValue_WrittenAsInteger()
    {
        var field = ScalarField("BigId", typeof(ulong), "BigId");
        ProjectedFieldPayloadWriter? writer = ProjectionPayloadWriterCompiler.TryCompile(
            typeof(PayloadSubject), "BigId", field);
        Assert.NotNull(writer);
        AssertPayloadField(writer!, new PayloadSubject { BigId = 42 }, "BigId",
            v => Assert.Equal(42, v.Integer));
    }

    private static FilterField ScalarField(string name, Type valueType, string propertyPath) =>
        new(name, valueType, FilterFieldKind.Scalar,
            _ => null, Access: FilterFieldAccess.ForProperty(propertyPath));

    private static void AssertPayloadField(
        ProjectedFieldPayloadWriter writer,
        object subject,
        string fieldName,
        Action<ProjectedEventValue> assertion)
    {
        var options = MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var mpWriter = new MessagePackWriter(buffer);

        mpWriter.WriteMapHeader(1);
        writer(ref mpWriter, subject, options);
        mpWriter.Flush();

        var reader = new MessagePackReader(buffer.WrittenMemory);
        int mapCount = reader.ReadMapHeader();
        Assert.Equal(1, mapCount);

        ProjectedEventField field = MessagePackSerializer.Deserialize<ProjectedEventField>(
            ref reader, options);
        Assert.Equal(fieldName, field.Name);
        assertion(field.Value);
    }

    public sealed class PayloadSubject
    {
        public bool IsActive { get; set; } = true;
        public bool? NullableBool { get; set; }
        public int Count { get; set; }
        public int? NullableCount { get; set; }
        public ulong BigId { get; set; }
        public ulong? NullableBigId { get; set; }
        public double Score { get; set; }
        public double? NullableScore { get; set; }
        public float Ratio { get; set; }
        public decimal Price { get; set; }
        public decimal? NullablePrice { get; set; }
        public string? Name { get; set; }
        public Guid Id { get; set; }
        public Guid? NullableId { get; set; }
        public NestedObj? Nested { get; set; }
        public DateTime Created { get; set; }
        public byte Level { get; set; }
        public short ShortVal { get; set; }
        public long LongVal { get; set; }
    }

    public sealed class NestedObj
    {
        public int Value { get; set; }
    }
}
