using SiftQL;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class GeneratedFilterSchemaRegistryTests
{
    [Fact]
    public void EnumToInt64OrNull_ReturnsValue_ForIntBackedEnum()
    {
        long? result = GeneratedFilterSchemaRegistry.EnumToInt64OrNull(IntEnum.B);
        Assert.Equal(2L, result);
    }

    [Fact]
    public void EnumToInt64OrNull_ReturnsNull_ForUlongBackedEnum()
    {
        long? result = GeneratedFilterSchemaRegistry.EnumToInt64OrNull(UlongEnum.X);
        Assert.Null(result);
    }

    [Fact]
    public void NullableEnumToInt64OrNull_ReturnsNull_WhenNull()
    {
        long? result = GeneratedFilterSchemaRegistry.NullableEnumToInt64OrNull<IntEnum>(null);
        Assert.Null(result);
    }

    [Fact]
    public void NullableEnumToInt64OrNull_ReturnsValue_WhenPresent()
    {
        long? result = GeneratedFilterSchemaRegistry.NullableEnumToInt64OrNull<IntEnum>(IntEnum.C);
        Assert.Equal(3L, result);
    }

    [Fact]
    public void NullableEnumToInt64OrNull_ReturnsNull_ForUlongBackedEnum_WhenPresent()
    {
        long? result = GeneratedFilterSchemaRegistry.NullableEnumToInt64OrNull<UlongEnum>(UlongEnum.X);
        Assert.Null(result);
    }

    [Fact]
    public void Create_ReturnsFilterSchema()
    {
        var fields = new List<FilterField>
        {
            new("TestField", typeof(int), FilterFieldKind.Scalar, _ => null),
        };
        FilterSchema schema = GeneratedFilterSchemaRegistry.Create(typeof(ItemUsedEvent), fields);
        Assert.Equal(typeof(ItemUsedEvent), schema.SubjectType);
        Assert.Single(schema.FieldNames);
        Assert.Contains("TestField", schema.FieldNames);
    }

    private enum IntEnum { A = 1, B = 2, C = 3 }
    private enum UlongEnum : ulong { X = 1, Y = 2 }
}
