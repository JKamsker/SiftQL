namespace SiftQL.Generators.Tests;

internal static class FilterRuntimeStubSource
{
    public const string Text = """
        using System;
        using System.Collections.Generic;

        namespace SiftQL
        {
            public enum ProjectedEventValueKind
            {
                Null,
                Boolean,
                Integer,
                Number,
                String,
                Guid,
                Array,
                Object,
                UnsignedInteger,
            }

            public sealed record ProjectedEventValue
            {
                public ProjectedEventValueKind Kind { get; init; }
                public ulong UnsignedInteger { get; init; }
                public string? String { get; init; }
            }

            internal static class ProjectionValueFactory
            {
                public static ProjectedEventValue FromBoolean(bool? value) => new();
                public static ProjectedEventValue FromByte(byte? value) => new();
                public static ProjectedEventValue FromSByte(sbyte? value) => new();
                public static ProjectedEventValue FromInt16(short? value) => new();
                public static ProjectedEventValue FromUInt16(ushort? value) => new();
                public static ProjectedEventValue FromInt32(int? value) => new();
                public static ProjectedEventValue FromUInt32(uint? value) => new();
                public static ProjectedEventValue FromInt64(long? value) => new();
                public static ProjectedEventValue FromUInt64(ulong? value) => new();
                public static ProjectedEventValue FromSingle(float? value) => new();
                public static ProjectedEventValue FromDouble(double? value) => new();
                public static ProjectedEventValue FromDecimal(decimal? value) => new();
                public static ProjectedEventValue FromString(string? value) => new();
                public static ProjectedEventValue FromGuid(Guid? value) => new();
                public static ProjectedEventValue FromObject(object? value) => new();
                public static ProjectedEventValue FromEnum<TEnum>(TEnum? value)
                    where TEnum : struct, Enum => new();
            }
        }

        namespace SiftQL.Schema
        {
            public enum FilterFieldKind { Scalar, Array, Object }
            public enum FilterScalarKind { Boolean, Number, String, Guid, Enum }

            public sealed record FilterField(
                string Name,
                Type ValueType,
                FilterFieldKind Kind,
                Func<object, object?> Getter,
                FilterScalarAccessor? ScalarAccessor = null,
                FilterArrayAccessor? ArrayAccessor = null,
                Func<object, SiftQL.ProjectedEventValue>? ProjectionAccessor = null,
                FilterFieldAccess? Access = null);

            public sealed record FilterFieldAccess(string? PropertyPath, object? ConstantValue = null)
            {
                public static FilterFieldAccess ForProperty(string propertyPath) => new(propertyPath);
                public static FilterFieldAccess ForConstant(object? value) => new(null, value);
            }

            public sealed class FilterScalarAccessor
            {
                public FilterScalarAccessor(
                    FilterScalarKind kind,
                    Func<object, bool?>? boolean = null,
                    Func<object, double?>? number = null,
                    Func<object, string?>? text = null,
                    Func<object, Guid?>? guid = null,
                    Func<object, long?>? enumeration = null,
                    Func<object, bool>? requiredBoolean = null,
                    Func<object, double>? requiredNumber = null,
                    Func<object, Guid>? requiredGuid = null,
                    Func<object, long>? requiredEnumeration = null) {}
            }

            public sealed class FilterArrayAccessor
            {
                public FilterArrayAccessor(
                    FilterScalarKind elementKind,
                    Func<object, bool, bool>? booleanContains = null,
                    Func<object, double, bool>? numberContains = null,
                    Func<object, string?, bool>? textContains = null,
                    Func<object, Guid, bool>? guidContains = null) {}
            }

            public sealed class FilterSchema
            {
                internal FilterSchema(Type subjectType, IReadOnlyList<FilterField> fields) {}
            }

            public delegate bool GeneratedFilterSchemaProviderDelegate(Type subjectType, out FilterSchema? schema);

            public static class GeneratedFilterSchemaRegistry
            {
                public static FilterSchema Create(Type subjectType, IReadOnlyList<FilterField> fields) => new(subjectType, fields);
                public static void Register(System.Reflection.Assembly assembly, GeneratedFilterSchemaProviderDelegate provider) {}
            }

            internal static class FilterArrayContains
            {
                public static bool ContainsBoolean(bool[]? items, bool expected) => false;
                public static bool ContainsByte(byte[]? items, double expected) => false;
                public static bool ContainsSByte(sbyte[]? items, double expected) => false;
                public static bool ContainsInt16(short[]? items, double expected) => false;
                public static bool ContainsUInt16(ushort[]? items, double expected) => false;
                public static bool ContainsInt32(int[]? items, double expected) => false;
                public static bool ContainsUInt32(uint[]? items, double expected) => false;
                public static bool ContainsInt64(long[]? items, double expected) => false;
                public static bool ContainsUInt64(ulong[]? items, double expected) => false;
                public static bool ContainsSingle(float[]? items, double expected) => false;
                public static bool ContainsDouble(double[]? items, double expected) => false;
                public static bool ContainsDecimal(decimal[]? items, double expected) => false;
                public static bool ContainsString(string?[]? items, string? expected) => false;
                public static bool ContainsGuid(Guid[]? items, Guid expected) => false;
            }
        }
        """;
}
