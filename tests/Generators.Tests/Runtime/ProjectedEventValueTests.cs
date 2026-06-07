using SiftQL.Projected;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectedEventValueTests
{
    [Fact]
    public void FromScalar_Null_ReturnsNull()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(null);
        Assert.Equal(ProjectedEventValueKind.Null, value.Kind);
    }

    [Fact]
    public void FromScalar_Bool_ReturnsBoolean()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(true);
        Assert.Equal(ProjectedEventValueKind.Boolean, value.Kind);
        Assert.True(value.Boolean);
    }

    [Fact]
    public void FromScalar_Byte_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((byte)42);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(42, value.Integer);
    }

    [Fact]
    public void FromScalar_SByte_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((sbyte)-7);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(-7, value.Integer);
    }

    [Fact]
    public void FromScalar_Short_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((short)1000);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(1000, value.Integer);
    }

    [Fact]
    public void FromScalar_UShort_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar((ushort)60000);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(60000, value.Integer);
    }

    [Fact]
    public void FromScalar_UInt_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(42u);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(42, value.Integer);
    }

    [Fact]
    public void FromScalar_ULong_SmallValue_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(100UL);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(100, value.Integer);
    }

    [Fact]
    public void FromScalar_ULong_LargeValue_ReturnsUnsignedInteger()
    {
        ulong large = (ulong)long.MaxValue + 1;
        ProjectedEventValue value = ProjectedEventValue.FromScalar(large);
        Assert.Equal(ProjectedEventValueKind.UnsignedInteger, value.Kind);
        Assert.Equal(large, value.UnsignedInteger);
    }

    [Fact]
    public void FromScalar_Float_ReturnsNumber()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(3.14f);
        Assert.Equal(ProjectedEventValueKind.Number, value.Kind);
        Assert.Equal(3.14f, value.Number, precision: 5);
    }

    [Fact]
    public void FromScalar_Double_ReturnsNumber()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(2.718);
        Assert.Equal(ProjectedEventValueKind.Number, value.Kind);
        Assert.Equal(2.718, value.Number);
    }

    [Fact]
    public void FromScalar_Decimal_Fractional_ReturnsDecimal()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(99.99m);
        Assert.Equal(ProjectedEventValueKind.Decimal, value.Kind);
    }

    [Fact]
    public void FromScalar_Decimal_WholeNumber_ReturnsInteger()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(42m);
        Assert.Equal(ProjectedEventValueKind.Integer, value.Kind);
        Assert.Equal(42, value.Integer);
    }

    [Fact]
    public void FromScalar_Guid_ReturnsGuid()
    {
        var guid = Guid.NewGuid();
        ProjectedEventValue value = ProjectedEventValue.FromScalar(guid);
        Assert.Equal(ProjectedEventValueKind.Guid, value.Kind);
        Assert.Equal(guid, value.Guid);
    }

    [Fact]
    public void FromScalar_Enum_ReturnsString()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(DayOfWeek.Friday);
        Assert.Equal(ProjectedEventValueKind.String, value.Kind);
        Assert.Equal("Friday", value.String);
    }

    [Fact]
    public void FromScalar_Array_ReturnsArray()
    {
        ProjectedEventValue value = ProjectedEventValue.FromScalar(new[] { 1, 2, 3 });
        Assert.Equal(ProjectedEventValueKind.Array, value.Kind);
        Assert.Equal(3, value.Values.Length);
        Assert.Equal(1, value.Values[0].Integer);
    }

    [Fact]
    public void FromScalar_Object_ReturnsObjectWithFields()
    {
        var obj = new SimpleObj { Name = "test", Value = 42 };
        ProjectedEventValue value = ProjectedEventValue.FromScalar(obj);
        Assert.Equal(ProjectedEventValueKind.Object, value.Kind);
        Assert.True(value.Fields.Length >= 2);
    }

    [Fact]
    public void FromObject_Null_ReturnsNull()
    {
        ProjectedEventValue value = ProjectedEventValue.FromObject(null);
        Assert.Equal(ProjectedEventValueKind.Null, value.Kind);
    }

    [Fact]
    public void FromObject_ReturnsObjectKind()
    {
        ProjectedEventValue value = ProjectedEventValue.FromObject(new SimpleObj { Name = "x" });
        Assert.Equal(ProjectedEventValueKind.Object, value.Kind);
    }

    [Fact]
    public void FromArray_Collection_ReturnsArray()
    {
        var list = new List<int> { 10, 20 };
        ProjectedEventValue value = ProjectedEventValue.FromArray(list);
        Assert.Equal(ProjectedEventValueKind.Array, value.Kind);
        Assert.Equal(2, value.Values.Length);
    }

    [Fact]
    public void FromArray_NonCollection_ReturnsArray()
    {
        ProjectedEventValue value = ProjectedEventValue.FromArray(Generate());
        Assert.Equal(ProjectedEventValueKind.Array, value.Kind);
        Assert.Equal(3, value.Values.Length);

        static IEnumerable<object> Generate()
        {
            yield return 1;
            yield return "two";
            yield return 3.0;
        }
    }

    [Fact]
    public void FromArray_WithNulls_PreservesNullValues()
    {
        var items = new object?[] { 1, null, 3 };
        ProjectedEventValue value = ProjectedEventValue.FromArray(items);
        Assert.Equal(3, value.Values.Length);
        Assert.Equal(ProjectedEventValueKind.Null, value.Values[1].Kind);
    }

    [Fact]
    public void FromValues_ReturnsArrayOfProjectedValues()
    {
        var values = new[]
        {
            ProjectedEventValue.FromScalar(1),
            ProjectedEventValue.FromScalar("two"),
        };
        ProjectedEventValue result = ProjectedEventValue.FromValues(values);
        Assert.Equal(ProjectedEventValueKind.Array, result.Kind);
        Assert.Equal(2, result.Values.Length);
    }

    [Fact]
    public void FromFields_ReturnsObjectKind()
    {
        var fields = new[]
        {
            new ProjectedEventField("a", ProjectedEventValue.FromScalar(1)),
        };
        ProjectedEventValue result = ProjectedEventValue.FromFields(fields);
        Assert.Equal(ProjectedEventValueKind.Object, result.Kind);
        Assert.Single(result.Fields);
    }

    public sealed class SimpleObj
    {
        public string? Name { get; set; }
        public int Value { get; set; }
    }
}
