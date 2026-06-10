using System.Linq;
using System.Text.Json.Nodes;
using SiftQL;
using SiftQL.Schema;
using Xunit;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaJsonSchemaTests
{
    [Fact]
    public void DescribeReportsTemporalFieldWithOrderedAndRangeOperators()
    {
        FilterSchemaDescriptor descriptor = FilterSchema.For(typeof(Audit)).Describe();
        FilterFieldDescriptor at = Field(descriptor, nameof(Audit.At));

        Assert.Equal("Temporal", at.ScalarKind);
        Assert.Contains("GreaterThan", at.Operators);
        Assert.Contains("Between", at.Operators);
    }

    [Fact]
    public void NumericFieldAdvertisesBetween()
    {
        FilterSchemaDescriptor descriptor = FilterSchema.For(typeof(Audit)).Describe();
        Assert.Contains("Between", Field(descriptor, nameof(Audit.Score)).Operators);
    }

    [Fact]
    public void ToJsonSchemaEmitsTypedProperties()
    {
        JsonObject schema = FilterSchema.For(typeof(Audit)).Describe().ToJsonSchema();

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        var properties = schema["properties"]!.AsObject();

        Assert.Equal("string", properties[nameof(Audit.Name)]!["type"]!.GetValue<string>());
        Assert.Equal("number", properties[nameof(Audit.Score)]!["type"]!.GetValue<string>());
        Assert.Equal("array", properties[nameof(Audit.Tags)]!["type"]!.GetValue<string>());

        JsonObject at = properties[nameof(Audit.At)]!.AsObject();
        Assert.Equal("string", at["type"]!.GetValue<string>());
        Assert.Equal("date-time", at["format"]!.GetValue<string>());
    }

    [Fact]
    public void ToJsonSchemaAnnotatesOperatorsPerField()
    {
        JsonObject schema = FilterSchema.For(typeof(Audit)).Describe().ToJsonSchema();
        var nameOperators = schema["properties"]![nameof(Audit.Name)]!["x-siftql-operators"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();

        Assert.Contains("StringStartsWith", nameOperators);
        Assert.Contains("Equal", nameOperators);
    }

    [Fact]
    public void DateOnlyFieldEmitsDateFormat()
    {
        JsonObject schema = FilterSchema.For(typeof(DayLog)).Describe().ToJsonSchema();
        JsonObject day = schema["properties"]![nameof(DayLog.Day)]!.AsObject();

        Assert.Equal("string", day["type"]!.GetValue<string>());
        Assert.Equal("date", day["format"]!.GetValue<string>());
    }

    private static FilterFieldDescriptor Field(FilterSchemaDescriptor descriptor, string name) =>
        descriptor.Fields.Single(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));

    private sealed record Audit(string Name, int Score, DateTimeOffset At, string[] Tags) : IFilterSubject;
    private sealed record DayLog(DateOnly Day) : IFilterSubject;
}
