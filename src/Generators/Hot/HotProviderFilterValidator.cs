using System.Collections.Immutable;
using SiftQL.Generators.Schema;

namespace SiftQL.Generators.Hot;

internal static class HotProviderFilterValidator
{
    private const int StringContainsOperator = 6;

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
        if (!TryRequireField(
                fields,
                projectedEvent,
                node.Field,
                scalar: true,
                path,
                diagnostics,
                out GeneratedScalarKind scalarKind,
                out bool projectedDynamic))
        {
            return false;
        }
        if (node.Value is null)
            return Unsupported(diagnostics, path, "Hot compare filters require a value.");

        return ValidateComparison(scalarKind, projectedDynamic, node.Operator, node.Value, diagnostics, path);
    }

    private static bool ValidateIn(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (!TryRequireField(
                fields,
                projectedEvent,
                node.Field,
                scalar: true,
                path,
                diagnostics,
                out GeneratedScalarKind scalarKind,
                out bool projectedDynamic))
        {
            return false;
        }
        if (node.Values.Count == 0)
            return Unsupported(diagnostics, path, "Hot in filters require at least one value.");
        if (node.Values.Count > 128)
            return Unsupported(diagnostics, path, "Hot in filters cannot contain more than 128 values.");

        for (int i = 0; i < node.Values.Count; i++)
        {
            if (!ValidateValue(scalarKind, projectedDynamic, node.Values[i], diagnostics, path))
                return false;
        }

        return true;
    }

    private static bool ValidateContains(
        HotFilterNode node,
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (!TryRequireField(
                fields,
                projectedEvent,
                node.Field,
                scalar: false,
                path,
                diagnostics,
                out GeneratedScalarKind scalarKind,
                out bool projectedDynamic))
        {
            return false;
        }
        if (node.Value is null)
            return Unsupported(diagnostics, path, "Hot contains filters require a value.");

        return ValidateValue(scalarKind, projectedDynamic, node.Value, diagnostics, path);
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

    private static bool TryRequireField(
        EquatableArray<GeneratedField> fields,
        bool projectedEvent,
        string name,
        bool? scalar,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        out GeneratedScalarKind scalarKind,
        out bool projectedDynamic)
    {
        scalarKind = GeneratedScalarKind.Object;
        projectedDynamic = false;
        if (!RequireField(fields, projectedEvent, name, scalar, path, diagnostics))
            return false;

        GeneratedField? field = fields.Items.FirstOrDefault(
            item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (field is not null)
        {
            scalarKind = field.ScalarKind;
            return true;
        }

        if (HotProviderFieldValidator.IsMetadataField(name))
        {
            scalarKind = GeneratedScalarKind.String;
            return true;
        }

        projectedDynamic = true;
        return true;
    }

    private static bool ValidateComparison(
        GeneratedScalarKind scalarKind,
        bool projectedDynamic,
        int op,
        HotFilterValue value,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (!ValidateValue(scalarKind, projectedDynamic, value, diagnostics, path))
            return false;
        if (op == StringContainsOperator)
        {
            return value.Kind == HotFilterValueKind.String &&
                (projectedDynamic || scalarKind == GeneratedScalarKind.String) ||
                Unsupported(diagnostics, path, "Hot string contains filters require a string field and value.");
        }

        if (op is 0 or 1)
            return true;
        if (projectedDynamic && IsNumeric(value.Kind))
            return true;
        return scalarKind == GeneratedScalarKind.Number ||
            Unsupported(diagnostics, path, "Hot ordered comparisons require a numeric field.");
    }

    private static bool ValidateValue(
        GeneratedScalarKind scalarKind,
        bool projectedDynamic,
        HotFilterValue value,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path)
    {
        if (value.Kind == HotFilterValueKind.Null || projectedDynamic)
            return true;

        bool valid = scalarKind switch
        {
            GeneratedScalarKind.Boolean => value.Kind == HotFilterValueKind.Boolean,
            GeneratedScalarKind.Number => IsNumeric(value.Kind),
            GeneratedScalarKind.String => value.Kind == HotFilterValueKind.String,
            GeneratedScalarKind.Guid => value.Kind == HotFilterValueKind.Guid,
            GeneratedScalarKind.Enum => value.Kind is HotFilterValueKind.String or
                HotFilterValueKind.Integer or
                HotFilterValueKind.UnsignedInteger,
            _ => false,
        };
        return valid ||
            Unsupported(diagnostics, path, $"Hot filter value '{value.Kind}' is not compatible with the field.");
    }

    private static bool IsNumeric(HotFilterValueKind kind) =>
        kind is HotFilterValueKind.Integer or
            HotFilterValueKind.UnsignedInteger or
            HotFilterValueKind.Number or
            HotFilterValueKind.Decimal;

    private static bool Unsupported(
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        string path,
        string message) =>
        HotProviderFieldValidator.Unsupported(diagnostics, path, message);
}
