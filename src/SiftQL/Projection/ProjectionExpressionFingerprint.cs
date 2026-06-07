using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using System.Text;
using SiftQL;
using SiftQL.Expressions;
using SiftQL.Values;

namespace SiftQL.Projection;

internal static class ProjectionExpressionFingerprint
{
    private static readonly ConditionalWeakTable<EventProjectionExpression, ProjectionExpressionKey> s_keys = new();

    public static ProjectionExpressionKey CreateKey(EventProjectionExpression projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return s_keys.GetValue(projection, static item => ProjectionExpressionKey.From(item));
    }

    public static string Create(EventProjectionExpression projection) =>
        CreateKey(projection).ToString();
}

internal sealed class ProjectionExpressionKey : IEquatable<ProjectionExpressionKey>
{
    private readonly int _hashCode;

    private ProjectionExpressionKey(
        StructuralKeyArray<ProjectionFieldKey> fields,
        StructuralKeyArray<ProjectionIncludeKey> includes)
    {
        Fields = fields;
        Includes = includes;
        _hashCode = HashCode.Combine(Fields, Includes);
    }

    public StructuralKeyArray<ProjectionFieldKey> Fields { get; }
    public StructuralKeyArray<ProjectionIncludeKey> Includes { get; }

    public static ProjectionExpressionKey From(EventProjectionExpression projection) =>
        new(
            projection.Fields.Length == 0
                ? StructuralKeyArray<ProjectionFieldKey>.Empty
                : StructuralKeyArray<ProjectionFieldKey>.From(projection.Fields, ProjectionFieldKey.From),
            projection.Includes.Length == 0
                ? StructuralKeyArray<ProjectionIncludeKey>.Empty
                : StructuralKeyArray<ProjectionIncludeKey>.From(projection.Includes, ProjectionIncludeKey.From));

    public bool Equals(ProjectionExpressionKey? other) =>
        ReferenceEquals(this, other) ||
        (other is not null &&
            Fields.Equals(other.Fields) &&
            Includes.Equals(other.Includes));

    public override bool Equals(object? obj) =>
        obj is ProjectionExpressionKey other && Equals(other);

    public override int GetHashCode() => _hashCode;

    public override string ToString()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ToDebugString()));
        return Convert.ToHexString(hash);
    }

    public string ToDebugString()
    {
        var builder = new StringBuilder();
        AppendTo(builder);
        return builder.ToString();
    }

    private void AppendTo(StringBuilder builder)
    {
        builder.Append("fields[").Append(Fields.Count).Append(']');
        for (int i = 0; i < Fields.Count; i++)
        {
            FilterKeyText.AppendText(builder, Fields[i].Path);
            builder.Append("=>");
            FilterKeyText.AppendText(builder, Fields[i].Name);
        }

        builder.Append("includes[").Append(Includes.Count).Append(']');
        for (int i = 0; i < Includes.Count; i++)
            Includes[i].AppendTo(builder);
    }
}

internal readonly record struct ProjectionFieldKey(string Path, string Name)
{
    public static ProjectionFieldKey From(EventProjectionField field) =>
        new(field.Path, field.Name);
}

internal readonly record struct ProjectionArgumentKey(string Name, FilterValueKey Value)
{
    public static ProjectionArgumentKey From(EventProjectionArgument argument) =>
        new(argument.Name, FilterValueKey.From(argument.Value));
}

internal readonly record struct ProjectionIncludeKey(
    string Intrinsic,
    string ResultName,
    StructuralKeyArray<ProjectionArgumentKey> Arguments)
{
    public static ProjectionIncludeKey From(EventProjectionInclude include) =>
        new(
            include.Intrinsic,
            include.ResultName,
            new StructuralKeyArray<ProjectionArgumentKey>(
                include.Arguments
                    .OrderBy(static item => item.Name, StringComparer.Ordinal)
                    .Select(ProjectionArgumentKey.From)));

    public void AppendTo(StringBuilder builder)
    {
        FilterKeyText.AppendText(builder, Intrinsic);
        builder.Append(':');
        FilterKeyText.AppendText(builder, ResultName);
        builder.Append("args[").Append(Arguments.Count).Append(']');
        for (int i = 0; i < Arguments.Count; i++)
        {
            FilterKeyText.AppendText(builder, Arguments[i].Name);
            builder.Append('=');
            Arguments[i].Value.AppendTo(builder);
        }
    }
}
