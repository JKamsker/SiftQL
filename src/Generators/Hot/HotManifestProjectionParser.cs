using System.Collections.Immutable;
using System.Text.Json;

namespace SiftQL.Generators.Hot;

internal static partial class HotManifestParser
{
    private static HotProjection? ParseProjection(
        JsonElement element,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics)
    {
        var fields = ImmutableArray.CreateBuilder<HotProjectionField>();
        if (element.TryGetProperty("Fields", out JsonElement fieldItems))
        {
            if (fieldItems.ValueKind != JsonValueKind.Array)
            {
                Add(diagnostics, "FSFHOT009", path, "Hot projection fields must be an array.");
                return null;
            }

            foreach (JsonElement item in fieldItems.EnumerateArray())
            {
                if (!RequireObject(item, path, "Hot projection field", diagnostics))
                    return null;

                string fieldPath = ReadString(item, "Path");
                fields.Add(new(ReadString(item, "Name", fieldPath), fieldPath));
            }
        }

        EquatableArray<HotProjectionInclude> includes = ParseProjectionIncludes(
            element,
            path,
            diagnostics,
            out bool includesValid);
        return includesValid
            ? new(new(fields.ToImmutable()), includes.Count != 0, HasProjectionParameters(includes), includes)
            : null;
    }

    private static EquatableArray<HotProjectionInclude> ParseProjectionIncludes(
        JsonElement element,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        out bool valid)
    {
        valid = true;
        if (!element.TryGetProperty("Includes", out JsonElement includes))
        {
            return EquatableArray<HotProjectionInclude>.Empty;
        }

        if (includes.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, "FSFHOT009", path, "Hot projection includes must be an array.");
            valid = false;
            return EquatableArray<HotProjectionInclude>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<HotProjectionInclude>();
        var resultNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement include in includes.EnumerateArray())
        {
            if (!RequireObject(include, path, "Hot projection include", diagnostics))
            {
                valid = false;
                continue;
            }

            string intrinsic = ReadString(include, "Intrinsic");
            string resultName = ReadString(include, "ResultName");
            if (string.IsNullOrWhiteSpace(intrinsic) ||
                string.IsNullOrWhiteSpace(resultName))
            {
                Add(diagnostics, "FSFHOT009", path, "Hot projection include intrinsic and result name are required.");
                valid = false;
                continue;
            }

            if (!resultNames.Add(resultName))
            {
                Add(diagnostics, "FSFHOT009", path, "Hot projection include result names must be unique.");
                valid = false;
                continue;
            }

            EquatableArray<HotProjectionArgument> arguments = ParseProjectionArguments(
                include,
                path,
                diagnostics,
                out bool argumentsValid);
            if (!argumentsValid)
            {
                valid = false;
                continue;
            }

            builder.Add(new(intrinsic, resultName, arguments));
        }

        return new(builder.ToImmutable());
    }

    private static EquatableArray<HotProjectionArgument> ParseProjectionArguments(
        JsonElement include,
        string path,
        ImmutableArray<HotProviderDiagnostic>.Builder diagnostics,
        out bool valid)
    {
        valid = true;
        if (!include.TryGetProperty("Arguments", out JsonElement arguments))
        {
            return EquatableArray<HotProjectionArgument>.Empty;
        }

        if (arguments.ValueKind != JsonValueKind.Array)
        {
            Add(diagnostics, "FSFHOT009", path, "Hot projection include arguments must be an array.");
            valid = false;
            return EquatableArray<HotProjectionArgument>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<HotProjectionArgument>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement argument in arguments.EnumerateArray())
        {
            if (!RequireObject(argument, path, "Hot projection argument", diagnostics))
            {
                valid = false;
                continue;
            }

            string name = ReadString(argument, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                Add(diagnostics, "FSFHOT009", path, "Hot projection argument name is required.");
                valid = false;
                continue;
            }

            if (!names.Add(name))
            {
                Add(diagnostics, "FSFHOT009", path, "Hot projection argument names must be unique.");
                valid = false;
                continue;
            }

            int kind = ReadInt(argument, "Kind");
            string sourcePath = ReadString(argument, "SourcePath");
            if (kind == (int)HotProjectionArgumentKind.SourceField)
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    Add(diagnostics, "FSFHOT009", path, "Hot projection source argument path is required.");
                    valid = false;
                    continue;
                }

                builder.Add(new(name, HotProjectionArgumentKind.SourceField, NullValue(), sourcePath));
                continue;
            }

            if (kind != (int)HotProjectionArgumentKind.Value)
            {
                Add(diagnostics, "FSFHOT009", path, $"Hot projection argument kind '{kind}' is not supported.");
                valid = false;
                continue;
            }

            if (!argument.TryGetProperty("Value", out JsonElement valueElement))
            {
                builder.Add(new(name, HotProjectionArgumentKind.Value, NullValue(), sourcePath));
                continue;
            }

            if (!RequireObject(valueElement, path, "Hot projection argument value", diagnostics))
            {
                valid = false;
                continue;
            }

            HotFilterValue? value = ParseValue(valueElement, path, diagnostics);
            if (value is null)
            {
                valid = false;
                continue;
            }

            builder.Add(new(name, HotProjectionArgumentKind.Value, value, sourcePath));
        }

        return new(builder.ToImmutable());
    }

    private static bool HasProjectionParameters(EquatableArray<HotProjectionInclude> includes)
    {
        for (int i = 0; i < includes.Count; i++)
        {
            EquatableArray<HotProjectionArgument> arguments = includes[i].Arguments;
            for (int j = 0; j < arguments.Count; j++)
            {
                if (arguments[j].Kind == HotProjectionArgumentKind.Value &&
                    !string.IsNullOrWhiteSpace(arguments[j].Value.ParameterKey))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static HotFilterValue NullValue() =>
        new(
            HotFilterValueKind.Null,
            ParameterKey: null,
            Boolean: false,
            Integer: 0,
            UnsignedInteger: 0,
            Number: 0,
            Decimal: 0,
            String: null,
            Guid: "00000000-0000-0000-0000-000000000000");
}
