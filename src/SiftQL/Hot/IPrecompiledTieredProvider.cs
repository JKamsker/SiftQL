using SiftQL;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Hot;

public delegate bool ParameterizedHotFilterPredicate(
    object subject,
    IReadOnlyList<FilterValue> parameters);

public delegate ProjectedEventField[] ParameterizedHotProjectionFields(
    object subject,
    IReadOnlyList<FilterValue> parameters);

public interface IPrecompiledTieredProvider
{
    bool TryGetFilter(
        Type subjectType,
        string fingerprint,
        out Func<object, bool>? predicate);

    bool TryGetParameterizedFilter(
        Type subjectType,
        string fingerprint,
        out ParameterizedHotFilterPredicate? predicate)
    {
        _ = subjectType;
        _ = fingerprint;
        predicate = null;
        return false;
    }

    bool TryGetProjection(
        Type subjectType,
        string fingerprint,
        out Func<object, ProjectedEventField[]>? projectFields);

    bool TryGetParameterizedProjection(
        Type subjectType,
        string fingerprint,
        out ParameterizedHotProjectionFields? projectFields)
    {
        _ = subjectType;
        _ = fingerprint;
        projectFields = null;
        return false;
    }
}
