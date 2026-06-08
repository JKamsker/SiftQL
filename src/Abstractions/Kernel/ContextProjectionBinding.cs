using SiftQL.Expressions;

namespace SiftQL;

internal sealed record ContextProjectionBinding(
    string Key,
    EventProjectionInclude Include);

internal sealed record ContextProjectionPlan(
    EventProjectionInclude[] NewIncludes,
    ContextProjectionBinding[] Bindings);
