using SiftQL.Projected;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class ProjectionValueFactoryTests
{
    public enum TestStatus { None = 0, Active = 1, Inactive = 2 }

    [Fact] public void ProjectionValueFactory_FromBoolean_True()
    {
        var v = ProjectionValueFactory.FromBoolean(true);
        Assert.Equal(ProjectedEventValueKind.Boolean, v.Kind);
        Assert.True(v.Boolean);
    }

    [Fact] public void ProjectionValueFactory_FromBoolean_NullableTrue() =>
        Assert.Equal(ProjectedEventValueKind.Boolean, ProjectionValueFactory.FromBoolean((bool?)true).Kind);

    [Fact] public void ProjectionValueFactory_FromBoolean_NullableNull() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromBoolean((bool?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromByte() =>
        Assert.Equal(5L, ProjectionValueFactory.FromByte((byte)5).Integer);

    [Fact] public void ProjectionValueFactory_FromByte_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromByte((byte?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromByte_Nullable_Value() =>
        Assert.Equal(10L, ProjectionValueFactory.FromByte((byte?)10).Integer);

    [Fact] public void ProjectionValueFactory_FromSByte() =>
        Assert.Equal(-3L, ProjectionValueFactory.FromSByte((sbyte)-3).Integer);

    [Fact] public void ProjectionValueFactory_FromSByte_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromSByte((sbyte?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromSByte_Nullable_Value() =>
        Assert.Equal(-5L, ProjectionValueFactory.FromSByte((sbyte?)-5).Integer);

    [Fact] public void ProjectionValueFactory_FromInt16() =>
        Assert.Equal(1000L, ProjectionValueFactory.FromInt16((short)1000).Integer);

    [Fact] public void ProjectionValueFactory_FromInt16_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromInt16((short?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromInt16_Nullable_Value() =>
        Assert.Equal(500L, ProjectionValueFactory.FromInt16((short?)500).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt16() =>
        Assert.Equal(2000L, ProjectionValueFactory.FromUInt16((ushort)2000).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt16_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromUInt16((ushort?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromUInt16_Nullable_Value() =>
        Assert.Equal(3000L, ProjectionValueFactory.FromUInt16((ushort?)3000).Integer);

    [Fact] public void ProjectionValueFactory_FromInt32() =>
        Assert.Equal(42L, ProjectionValueFactory.FromInt32(42).Integer);

    [Fact] public void ProjectionValueFactory_FromInt32_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromInt32((int?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromInt32_Nullable_Value() =>
        Assert.Equal(77L, ProjectionValueFactory.FromInt32((int?)77).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt32() =>
        Assert.Equal(99L, ProjectionValueFactory.FromUInt32(99u).Integer);

    [Fact] public void ProjectionValueFactory_FromUInt32_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromUInt32((uint?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromUInt32_Nullable_Value() =>
        Assert.Equal(55L, ProjectionValueFactory.FromUInt32((uint?)55u).Integer);

    [Fact] public void ProjectionValueFactory_FromInt64() =>
        Assert.Equal(123L, ProjectionValueFactory.FromInt64(123L).Integer);

    [Fact] public void ProjectionValueFactory_FromInt64_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromInt64((long?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromInt64_Nullable_Value() =>
        Assert.Equal(999L, ProjectionValueFactory.FromInt64((long?)999L).Integer);

    [Fact]
    public void ProjectionValueFactory_FromUInt64_WithinLongRange()
    {
        var v = ProjectionValueFactory.FromUInt64(500UL);
        Assert.Equal(ProjectedEventValueKind.Integer, v.Kind);
        Assert.Equal(500L, v.Integer);
    }

    [Fact]
    public void ProjectionValueFactory_FromUInt64_BeyondLongMax()
    {
        ulong big = (ulong)long.MaxValue + 1UL;
        var v = ProjectionValueFactory.FromUInt64(big);
        Assert.Equal(ProjectedEventValueKind.UnsignedInteger, v.Kind);
        Assert.Equal(big, v.UnsignedInteger);
    }

    [Fact] public void ProjectionValueFactory_FromUInt64_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromUInt64((ulong?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromUInt64_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Integer, ProjectionValueFactory.FromUInt64((ulong?)10UL).Kind);

    [Fact] public void ProjectionValueFactory_FromSingle() =>
        Assert.Equal(ProjectedEventValueKind.Number, ProjectionValueFactory.FromSingle(1.5f).Kind);

    [Fact] public void ProjectionValueFactory_FromSingle_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromSingle((float?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromSingle_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Number, ProjectionValueFactory.FromSingle((float?)2.5f).Kind);

    [Fact] public void ProjectionValueFactory_FromDouble()
    {
        var v = ProjectionValueFactory.FromDouble(3.14);
        Assert.Equal(ProjectedEventValueKind.Number, v.Kind);
        Assert.Equal(3.14, v.Number);
    }

    [Fact] public void ProjectionValueFactory_FromDouble_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromDouble((double?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromDouble_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Number, ProjectionValueFactory.FromDouble((double?)1.0).Kind);

    [Fact]
    public void ProjectionValueFactory_FromDecimal_Integral()
    {
        var v = ProjectionValueFactory.FromDecimal(42m);
        Assert.Equal(ProjectedEventValueKind.Integer, v.Kind);
        Assert.Equal(42L, v.Integer);
    }

    [Fact]
    public void ProjectionValueFactory_FromDecimal_Fractional() =>
        Assert.Equal(ProjectedEventValueKind.Decimal, ProjectionValueFactory.FromDecimal(1.5m).Kind);

    [Fact] public void ProjectionValueFactory_FromDecimal_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromDecimal((decimal?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromDecimal_Nullable_Value() =>
        Assert.Equal(ProjectedEventValueKind.Decimal, ProjectionValueFactory.FromDecimal((decimal?)1.5m).Kind);

    [Fact] public void ProjectionValueFactory_FromString_NonNull()
    {
        var v = ProjectionValueFactory.FromString("hello");
        Assert.Equal(ProjectedEventValueKind.String, v.Kind);
        Assert.Equal("hello", v.String);
    }

    [Fact] public void ProjectionValueFactory_FromString_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromString(null).Kind);

    [Fact]
    public void ProjectionValueFactory_FromGuid()
    {
        var g = Guid.NewGuid();
        var v = ProjectionValueFactory.FromGuid(g);
        Assert.Equal(ProjectedEventValueKind.Guid, v.Kind);
        Assert.Equal(g, v.Guid);
    }

    [Fact] public void ProjectionValueFactory_FromGuid_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromGuid((Guid?)null).Kind);

    [Fact]
    public void ProjectionValueFactory_FromGuid_Nullable_Value()
    {
        var g = Guid.NewGuid();
        var v = ProjectionValueFactory.FromGuid((Guid?)g);
        Assert.Equal(ProjectedEventValueKind.Guid, v.Kind);
        Assert.Equal(g, v.Guid);
    }

    [Fact] public void ProjectionValueFactory_FromEnum_ProducesString()
    {
        var v = ProjectionValueFactory.FromEnum(TestStatus.Active);
        Assert.Equal(ProjectedEventValueKind.String, v.Kind);
        Assert.Equal("Active", v.String);
    }

    [Fact] public void ProjectionValueFactory_FromEnum_Nullable_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromEnum((TestStatus?)null).Kind);

    [Fact] public void ProjectionValueFactory_FromEnum_Nullable_Value() =>
        Assert.Equal("Inactive", ProjectionValueFactory.FromEnum((TestStatus?)TestStatus.Inactive).String);

    [Fact] public void ProjectionValueFactory_FromObject_Null() =>
        Assert.Equal(ProjectedEventValueKind.Null, ProjectionValueFactory.FromObject(null).Kind);

    [Fact] public void ProjectionValueFactory_FromObject_String() =>
        Assert.Equal(ProjectedEventValueKind.Object, ProjectionValueFactory.FromObject("hi").Kind);

    [Fact] public void ProjectionValueFactory_FromObject_Int() =>
        Assert.Equal(ProjectedEventValueKind.Object, ProjectionValueFactory.FromObject(42).Kind);

    [Fact] public void ProjectionValueFactory_FromObject_Bool() =>
        Assert.Equal(ProjectedEventValueKind.Object, ProjectionValueFactory.FromObject(true).Kind);
}
