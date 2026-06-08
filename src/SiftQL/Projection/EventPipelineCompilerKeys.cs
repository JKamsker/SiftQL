using System.Reflection;
using System.Runtime.CompilerServices;
using SiftQL.Compiler;
using SiftQL.Expressions;

namespace SiftQL.Projection;

internal readonly record struct EventPipelineCacheKey(
    Type ContextType,
    Type SubjectType,
    EventPipelineExpressionKey Pipeline,
    IncludeCompilerKey IncludeCompiler,
    int PrecompiledProviderVersion,
    int SchemaVersion,
    FilterCompilerOptionsCacheKey FilterOptions,
    ProjectionCompilerOptionsCacheKey ProjectionOptions);

internal readonly struct IncludeCompilerKey : IEquatable<IncludeCompilerKey>
{
    private readonly MethodInfo _method;
    private readonly object? _target;
    private readonly int _hashCode;

    private IncludeCompilerKey(MethodInfo method, object? target)
    {
        _method = method;
        _target = target;
        _hashCode = HashCode.Combine(method, target is null ? 0 : RuntimeHelpers.GetHashCode(target));
    }

    public static IncludeCompilerKey From(Delegate includeCompiler) =>
        new(includeCompiler.Method, includeCompiler.Target);

    public bool Equals(IncludeCompilerKey other) =>
        _method == other._method && ReferenceEquals(_target, other._target);

    public override bool Equals(object? obj) =>
        obj is IncludeCompilerKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        string target = _target is null
            ? "static"
            : RuntimeHelpers.GetHashCode(_target).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        return string.Concat(
            _method.Module.ModuleVersionId.ToString("N"),
            ":",
            _method.MetadataToken.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ":",
            target);
    }
}
