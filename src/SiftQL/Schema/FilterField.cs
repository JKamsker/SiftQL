using SiftQL;
using SiftQL.Projected;

namespace SiftQL.Schema;

public enum FilterFieldKind
{
    Scalar,
    Array,
    Object,
}

public enum FilterScalarKind
{
    Boolean,
    Number,
    String,
    Guid,
    Enum,
}

public sealed record FilterField(
    string Name,
    Type ValueType,
    FilterFieldKind Kind,
    Func<object, object?> Getter,
    FilterScalarAccessor? ScalarAccessor = null,
    FilterArrayAccessor? ArrayAccessor = null,
    Func<object, ProjectedEventValue>? ProjectionAccessor = null,
    FilterFieldAccess? Access = null,
    bool IsCollectionDerived = false);
