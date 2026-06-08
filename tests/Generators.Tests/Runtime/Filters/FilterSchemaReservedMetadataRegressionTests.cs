using SiftQL.Compiler;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class FilterSchemaReservedMetadataRegressionTests
{
    [Theory]
    [InlineData(typeof(SubjectTypeCollision), nameof(SubjectTypeCollision.SubjectType))]
    [InlineData(typeof(SubjectNameCollision), nameof(SubjectNameCollision.SubjectName))]
    public void FallbackSchemaRejectsTopLevelMetadataPropertyCollision(
        Type subjectType,
        string propertyName)
    {
        FilterValidationException ex = Assert.Throws<FilterValidationException>(() =>
            FilterSchema.For(subjectType));

        Assert.Contains(propertyName, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SubjectTypeCollision(string SubjectType) : IFilterSubject;

    private sealed record SubjectNameCollision(string SubjectName) : IFilterSubject;
}
