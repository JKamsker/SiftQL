using System.Globalization;
using SiftQL;
using SiftQL.Compiler;
using SiftQL.Expressions;
using SiftQL.Hot;
using SiftQL.Projected;
using SiftQL.Schema;
using SiftQL.Values;

namespace SiftQL.Projection;

public static class ProjectionCompiler
{
    private const int MaxFields = 64;
    private const int MaxIncludes = 8;

    public static CompiledProjection<TContext> Compile<TContext>(
        Type subjectType,
        EventProjectionExpression? projection,
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> compileInclude,
        Func<string, Exception>? errorFactory = null) =>
        Compile(
            subjectType,
            projection,
            compileInclude,
            ProjectionCompilerOptions.Immediate,
            errorFactory);

    public static CompiledProjection<TContext> Compile<TContext>(
        Type subjectType,
        EventProjectionExpression? projection,
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> compileInclude,
        ProjectionCompilerOptions options,
        Func<string, Exception>? errorFactory = null)
    {
        EventProjectionExpression normalized = projection ?? EventProjectionExpression.Default;
        Func<Type, FilterSchema> schemaFactory = subjectType == typeof(ProjectedEvent)
            ? _ => ProjectedEventFilterSchema.ForProjection(normalized)
            : FilterSchema.For;
        return CompileWithSchema(subjectType, normalized, compileInclude, options, errorFactory, schemaFactory);
    }

    internal static CompiledProjection<TContext> CompileWithSchema<TContext>(
        Type subjectType,
        EventProjectionExpression? projection,
        Func<FilterSchema, EventProjectionInclude, CompiledProjection<TContext>.IncludeProjector> compileInclude,
        ProjectionCompilerOptions options,
        Func<string, Exception>? errorFactory,
        Func<Type, FilterSchema> schemaFactory,
        Type? eventMetadataType = null)
    {
        ArgumentNullException.ThrowIfNull(compileInclude);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(schemaFactory);
        projection ??= EventProjectionExpression.Default;
        if (projection.Fields.Length > MaxFields)
            throw Error(errorFactory, $"Projection exceeds the {MaxFields} field limit.");
        if (projection.Includes.Length > MaxIncludes)
            throw Error(errorFactory, $"Projection exceeds the {MaxIncludes} include limit.");
        ValidateIncludes(projection.Includes, errorFactory);

        var schema = schemaFactory(subjectType);
        CompileFields<TContext>(
            schema,
            projection,
            errorFactory,
            out var schemaFields,
            out var fields);
        var includes = projection.Includes.Select(include => compileInclude(schema, include)).ToArray();
        ProjectionExpressionKey projectionKey = ProjectionExpressionFingerprint.CreateKey(projection);
        string? fingerprint = null;
        FilterValue[]? parameters = ProjectionExpressionParameters.HasParameters(projection)
            ? ProjectionExpressionParameters.BindValues(projection, ProjectionExpressionParameters.Keys(projection))
            : null;
        Func<object, ProjectedEventField[]>? projectFields = null;
        bool hasPrecompiled = PrecompiledTieredProviderRegistry.HasProviders &&
            TryGetPrecompiledProjection(
            schema.SubjectType,
            Fingerprint(),
            parameters,
            out projectFields);
        if (hasPrecompiled)
        {
            return new CompiledProjection<TContext>(
                BuildKey(fields, projection.Includes),
                subjectType,
                eventMetadataType ?? subjectType,
                fields,
                includes,
                projectFields,
                tieredState: null);
        }

        if (options.Mode != ProjectionCompilationMode.Tiered)
            projectFields = ProjectionFieldArrayCompiler.TryCompile(schema.SubjectType, fields, schemaFields);

        Func<Func<object, ProjectedEventField[]>?> compileProjectFields =
            () => PrecompiledTieredProviderRegistry.HasProviders &&
                TryGetPrecompiledProjection(schema.SubjectType, Fingerprint(), parameters, out var precompiled)
                ? precompiled
                : ProjectionFieldArrayCompiler.TryCompile(schema.SubjectType, fields, schemaFields);
        Action<TieredProjectionSnapshot>? recordHot = options.HotManifestSink is null
            ? null
            : snapshot => options.HotManifestSink.RecordHotProjection(
                schema.SubjectType,
                projection,
                snapshot.Materializations,
                snapshot.PayloadWrites);
        CompiledProjection<TContext>? compiledProjection = null;
        var tieredState = options.Mode == ProjectionCompilationMode.Tiered
            ? new TieredProjectionState<TContext>(
                compileProjectFields,
                options.CreatePromotionPolicy(),
                recordHot,
                projectFields => compiledProjection!.PromoteProjectFields(projectFields))
            : null;
        compiledProjection = new CompiledProjection<TContext>(
            BuildKey(fields, projection.Includes),
            subjectType,
            eventMetadataType ?? subjectType,
            fields,
            includes,
            projectFields,
            tieredState);
        return compiledProjection;

        string Fingerprint() => fingerprint ??= projectionKey.ToString();
    }

    private static bool TryGetPrecompiledProjection(
        Type subjectType,
        string fingerprint,
        FilterValue[]? parameters,
        out Func<object, ProjectedEventField[]>? projectFields) =>
        parameters is null
            ? PrecompiledTieredProviderRegistry.TryGetProjection(subjectType, fingerprint, out projectFields)
            : PrecompiledTieredProviderRegistry.TryGetParameterizedProjection(
                subjectType,
                fingerprint,
                parameters,
                out projectFields);

    private static void CompileFields<TContext>(
        FilterSchema schema,
        EventProjectionExpression projection,
        Func<string, Exception>? errorFactory,
        out FilterField[] schemaFields,
        out CompiledProjection<TContext>.FieldProjector[] fields)
    {
        EventProjectionField[] requested = projection.Fields.Length == 0
            ? schema.FieldNames
                .Where(name => IsDefaultProjectionField(schema, name))
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(static name => new EventProjectionField(name))
                .ToArray()
            : projection.Fields;
        if (requested.Length > MaxFields)
            throw Error(errorFactory, $"Projection exceeds the {MaxFields} field limit.");

        var compiled = new CompiledProjection<TContext>.FieldProjector[requested.Length];
        schemaFields = new FilterField[requested.Length];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < requested.Length; i++)
        {
            var field = requested[i];
            if (field is null)
                throw Error(errorFactory, "Projection fields cannot be null.");
            if (string.IsNullOrWhiteSpace(field.Name) || string.IsNullOrWhiteSpace(field.Path))
                throw Error(errorFactory, "Projection fields require a name and path.");
            if (!names.Add(field.Name))
                throw Error(errorFactory, $"Projection field '{field.Name}' is duplicated.");
            if (!schema.TryGetField(field.Path, out var schemaField))
            {
                throw Error(
                    errorFactory,
                    $"Projection field '{field.Path}' is not supported by {schema.SubjectType.FullName}.");
            }

            schemaFields[i] = schemaField;
            compiled[i] = new CompiledProjection<TContext>.FieldProjector(
                field.Name,
                field.Path,
                schemaField.ProjectionAccessor ??
                    (subject => ProjectedEventValue.FromScalar(schemaField.Getter(subject))))
            {
                WritePayload = schemaField.ProjectionAccessor is null
                    ? ProjectionPayloadWriterCompiler.TryCompile(
                        schema.SubjectType,
                        field.Name,
                        schemaField)
                    : null,
            };
        }

        fields = compiled;
    }

    private static void ValidateIncludes(
        IReadOnlyList<EventProjectionInclude> includes,
        Func<string, Exception>? errorFactory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < includes.Count; i++)
        {
            var include = includes[i];
            if (include is null)
                throw Error(errorFactory, "Projection includes cannot be null.");
            if (string.IsNullOrWhiteSpace(include.Intrinsic) || string.IsNullOrWhiteSpace(include.ResultName))
                throw Error(errorFactory, "Projection includes require an intrinsic and result name.");
            if (include.Arguments is null)
                throw Error(errorFactory, "Projection include arguments cannot be null.");
            ValidateArguments(include, errorFactory);
            if (!names.Add(include.ResultName))
                throw Error(errorFactory, $"Projection include result '{include.ResultName}' is duplicated.");
        }
    }

    private static void ValidateArguments(
        EventProjectionInclude include,
        Func<string, Exception>? errorFactory)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < include.Arguments.Length; i++)
        {
            var argument = include.Arguments[i];
            if (argument is null)
                throw Error(errorFactory, $"Projection include '{include.Intrinsic}' arguments cannot contain null.");
            if (string.IsNullOrWhiteSpace(argument.Name))
                throw Error(errorFactory, $"Projection include '{include.Intrinsic}' arguments require a name.");
            if (argument.Value is null)
                throw Error(errorFactory, $"Projection include '{include.Intrinsic}' argument '{argument.Name}' is null.");
            if (!names.Add(argument.Name))
                throw Error(errorFactory, $"Projection include '{include.Intrinsic}' argument '{argument.Name}' is duplicated.");
        }
    }

    private static string BuildKey<TContext>(
        IReadOnlyList<CompiledProjection<TContext>.FieldProjector> fields,
        IReadOnlyList<EventProjectionInclude> includes)
    {
        string fieldKey = string.Concat(fields.Select(FieldKey));
        string includeKey = string.Concat(includes.Select(IncludeKey));
        return string.Concat("F", CountPart(fields.Count), fieldKey, "I", CountPart(includes.Count), includeKey);
    }

    private static string FieldKey<TContext>(CompiledProjection<TContext>.FieldProjector field) =>
        string.Concat("f", KeyPart(field.Path), KeyPart(field.Name));

    private static string IncludeKey(EventProjectionInclude include)
    {
        string args = string.Concat(include.Arguments
            .OrderBy(static arg => arg.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ArgumentKey));
        return string.Concat(
            "i",
            KeyPart(include.Intrinsic),
            KeyPart(include.ResultName),
            CountPart(include.Arguments.Length),
            args);
    }

    private static string ArgumentKey(EventProjectionArgument argument) =>
        string.Concat("a", KeyPart(argument.Name), KeyPart(FilterValueKey.From(argument.Value).ToString()));

    private static string CountPart(int count) =>
        count.ToString(CultureInfo.InvariantCulture) + ":";

    private static string KeyPart(string? value) =>
        value is null
            ? "-1:"
            : string.Concat(value.Length.ToString(CultureInfo.InvariantCulture), ":", value);

    private static bool IsDefaultProjectionField(FilterSchema schema, string name) =>
        !IsMetadataField(name) &&
        schema.TryGetField(name, out FilterField field) &&
        field.Kind != FilterFieldKind.Object;

    private static bool IsMetadataField(string name) =>
        string.Equals(name, "eventType", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "eventName", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "subjectType", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "subjectName", StringComparison.OrdinalIgnoreCase);

    private static Exception Error(Func<string, Exception>? errorFactory, string message) =>
        errorFactory?.Invoke(message) ?? new FilterValidationException(message);
}
