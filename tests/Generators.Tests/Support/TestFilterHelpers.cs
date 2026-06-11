using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Schema;

namespace SiftQL.Generators.Tests;

internal static class TestFilterHelpers
{
    public static string Fingerprint(FilterExpression expression) =>
        FilterExpressionFingerprint.Create(expression);

    public static FilterField ReservedField(string name, Func<object, string> value) =>
        new(
            name,
            typeof(string),
            FilterFieldKind.Scalar,
            value,
            new FilterScalarAccessor(FilterScalarKind.String, text: value));
}

internal sealed class AlwaysMatchingHotProvider(
    Type subjectType,
    string acceptedFingerprint) : IPrecompiledTieredProvider
{
    public bool TryGetFilter(
        Type type,
        string fingerprint,
        out Func<object, bool>? predicate)
    {
        if (type == subjectType && string.Equals(fingerprint, acceptedFingerprint, StringComparison.Ordinal))
        {
            predicate = static _ => true;
            return true;
        }

        predicate = null;
        return false;
    }

    public bool TryGetProjection(
        Type type,
        string fingerprint,
        out Func<object, ProjectedEventField[]>? projectFields)
    {
        _ = type;
        _ = fingerprint;
        projectFields = null;
        return false;
    }
}
