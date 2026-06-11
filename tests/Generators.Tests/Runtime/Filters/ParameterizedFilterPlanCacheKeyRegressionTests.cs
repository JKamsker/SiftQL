using System.Reflection;
using SiftQL.Parameterized;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

public sealed class ParameterizedFilterPlanCacheKeyRegressionTests
{
    [Fact]
    public void PlanCacheKeyCarriesSchemaReferenceIdentity()
    {
        Type keyType = typeof(ParameterizedFilterPlanCache).Assembly.GetType(
            "SiftQL.Parameterized.ParameterizedFilterPlanCacheKey",
            throwOnError: true)!;
        PropertyInfo[] properties = keyType.GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Contains(properties, static property => property.PropertyType == typeof(FilterSchema));
        Assert.DoesNotContain(properties, static property =>
            property.Name == "SchemaIdentity" &&
            property.PropertyType == typeof(int));
    }
}
