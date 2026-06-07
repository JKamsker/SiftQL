using System.Collections.Immutable;
using SiftQL.Generators.Schema;

namespace SiftQL.Generators.Hot;

internal static class HotProviderFilterValidator
{
    public static bool Validate(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        int nodes = 0;
        return Validate(node, fields, projectedEvent, diagnostics, path, ref nodes, depth: 0);
    }

    private static bool Validate(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path,
        ref int nodes,
        int depth)
    {
        if (nodes > 128)
            return false;

        nodes++;
        if (nodes > 128 || depth > 16)
            return Unsupported(diagnostics, path, "Hot filter exceeds runtime node or depth limits.");

        return node.Kind switch
        {
            HotFilterNodeKind.Any => true,
            HotFilterNodeKind.Compare => ValidateCompare(node, fields, projectedEvent, diagnostics, path),
            HotFilterNodeKind.In => ValidateIn(node, fields, projectedEvent, diagnostics, path),
            HotFilterNodeKind.Exists => RequireField(fields, projectedEvent, node.Field, scalar: null, path, diagnostics),
            HotFilterNodeKind.Contains => ValidateContains(node, fields, projectedEvent, diagnostics, path),
            HotFilterNodeKind.Not => ValidateNot(node, fields, projectedEvent, diagnostics, path, ref nodes, depth),
            HotFilterNodeKind.And or HotFilterNodeKind.Or =>
                ValidateChildren(node, fields, projectedEvent, diagnostics, path, ref nodes, depth),
            _ => Unsupported(diagnostics, path, $"Hot filter node kind '{node.Kind}' is not supported."),
        };
    }

    private static bool ValidateCompare(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (!RequireField(fields, projectedEvent, node.Field, scalar: true, path, diagnostics))
            return false;
        return node.Value is not null ||
            Unsupported(diagnostics, path, "Hot compare filters require a value.");
    }

    private static bool ValidateIn(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (!RequireField(fields, projectedEvent, node.Field, scalar: true, path, diagnostics))
            return false;
        if (node.Values.Count == 0)
            return Unsupported(diagnostics, path, "Hot in filters require at least one value.");
        return node.Values.Count <= 128 ||
            Unsupported(diagnostics, path, "Hot in filters cannot contain more than 128 values.");
    }

    private static bool ValidateContains(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (!RequireField(fields, projectedEvent, node.Field, scalar: false, path, diagnostics))
            return false;
        return node.Value is not null ||
            Unsupported(diagnostics, path, "Hot contains filters require a value.");
    }

    private static bool ValidateNot(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path,
        ref int nodes,
        int depth)
    {
        if (node.Children.Count != 1)
            return Unsupported(diagnostics, path, "Hot not filters must have exactly one child.");

        return Validate(node.Children[0], fields, projectedEvent, diagnostics, path, ref nodes, depth + 1);
    }

    private static bool ValidateChildren(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path,
        ref int nodes,
        int depth)
    {
        if (node.Children.Count == 0)
            return Unsupported(diagnostics, path, "Hot composite filters must have at least one child.");

        bool valid = true;
        for (int i = 0; i < node.Children.Count; i++)
            valid &= Validate(node.Children[i], fields, projectedEvent, diagnostics, path, ref nodes, depth + 1);

        return valid;
    }

    private static bool RequireField(
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        string name,
        bool? scalar,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics) =>
        HotProviderFieldValidator.RequireField(fields, projectedEvent, name, scalar, path, diagnostics);

    private static bool Unsupported(
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path,
        string message) =>
        HotProviderFieldValidator.Unsupported(diagnostics, path, message);
}
