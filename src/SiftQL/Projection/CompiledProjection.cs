using SiftQL;
using SiftQL.Projected;
using MessagePack;

namespace SiftQL.Projection;

public sealed class CompiledProjection<TContext>
{
    private static readonly ProjectedEventField[] s_emptyContext = [];
    private readonly FieldProjector[] _fields;
    private readonly IncludeProjector[] _includes;
    private readonly ProjectionEventMetadata _metadata;
    private readonly TieredProjectionState<TContext>? _tieredState;
    private Func<object, ProjectedEventField[]>? _projectFields;

    public CompiledProjection(
        string key,
        Type subjectType,
        IReadOnlyList<FieldProjector> fields,
        IReadOnlyList<IncludeProjector> includes,
        Func<object, ProjectedEventField[]>? projectFields = null)
        : this(key, subjectType, subjectType, fields, includes, projectFields, tieredState: null)
    {
    }

    internal CompiledProjection(
        string key,
        Type subjectType,
        IReadOnlyList<FieldProjector> fields,
        IReadOnlyList<IncludeProjector> includes,
        Func<object, ProjectedEventField[]>? projectFields,
        TieredProjectionState<TContext>? tieredState)
        : this(key, subjectType, subjectType, fields, includes, projectFields, tieredState)
    {
    }

    internal CompiledProjection(
        string key,
        Type subjectType,
        Type eventMetadataType,
        IReadOnlyList<FieldProjector> fields,
        IReadOnlyList<IncludeProjector> includes,
        Func<object, ProjectedEventField[]>? projectFields,
        TieredProjectionState<TContext>? tieredState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(eventMetadataType);
        Key = key;
        _metadata = new ProjectionEventMetadata(eventMetadataType);
        _fields = fields.ToArray();
        _includes = includes.ToArray();
        _projectFields = projectFields;
        _tieredState = tieredState;
        ValidateFields(_fields);
        ValidateIncludes(_includes);
    }

    public string Key { get; }
    public bool IsTiered => _tieredState is not null;
    public TieredProjectionSnapshot? TieredSnapshot => _tieredState?.Snapshot;

    public ValueTask<ProjectedEvent> ProjectAsync(
        object subject,
        TContext context,
        CancellationToken cancellationToken)
    {
        if (_includes.Length != 0)
        {
            _tieredState?.RecordMaterialization();
            return ProjectWithIncludesAsync(subject, context, cancellationToken);
        }

        Func<object, ProjectedEventField[]>? projectFields = Volatile.Read(ref _projectFields);
        if (projectFields is not null)
            return new ValueTask<ProjectedEvent>(ProjectComposedFields(subject, projectFields));

        _tieredState?.RecordMaterialization();
        return new ValueTask<ProjectedEvent>(ProjectFields(subject));
    }

    public ValueTask<ReadOnlyMemory<byte>> ProjectPayloadAsync(
        object subject,
        TContext context,
        MessagePackSerializerOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_includes.Length != 0)
        {
            _tieredState?.RecordPayloadWrite();
            return ProjectPayloadWithIncludesAsync(subject, context, options, cancellationToken);
        }

        if (Volatile.Read(ref _projectFields) is null)
            _tieredState?.RecordPayloadWrite();

        return new ValueTask<ReadOnlyMemory<byte>>(WritePayload(subject, InheritedContext(subject), options));
    }

    private ProjectedEvent ProjectFields(object subject) =>
        _metadata.Create(subject, ProjectFieldArray(subject), InheritedContext(subject));

    private ProjectedEvent ProjectFields(object subject, ProjectedEventField[] includes) =>
        _metadata.Create(subject, ProjectFieldArray(subject), includes);

    private ProjectedEvent ProjectComposedFields(
        object subject,
        Func<object, ProjectedEventField[]> projectFields) =>
        _metadata.Create(subject, projectFields(subject), InheritedContext(subject));

    private ProjectedEventField[] ProjectFieldArray(object subject) =>
        Volatile.Read(ref _projectFields) is { } projectFields
            ? projectFields(subject)
            : ProjectInterpretedFieldArray(subject);

    private ProjectedEventField[] ProjectInterpretedFieldArray(object subject)
    {
        var fields = new ProjectedEventField[_fields.Length];
        for (int i = 0; i < fields.Length; i++)
            fields[i] = Project(_fields[i], subject);
        return fields;
    }

    private ProjectedEventField[] InheritedContext(object subject)
    {
        if (subject is not ProjectedEvent projected ||
            projected.Context is not { Length: > 0 } contextFields)
        {
            return s_emptyContext;
        }

        HashSet<string>? consumed = null;
        for (int i = 0; i < _fields.Length; i++)
        {
            if (!ProjectedEventPaths.TrySplit(_fields[i].Path, out bool context, out string name) ||
                !context)
            {
                continue;
            }

            consumed ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            consumed.Add(ContextRoot(name));
        }

        if (consumed is null)
            return contextFields.ToArray();

        return contextFields
            .Where(field => !consumed.Contains(field.Name))
            .ToArray();
    }

    private static string ContextRoot(string name)
    {
        int separator = name.IndexOf('.');
        return separator < 0 ? name : name[..separator];
    }

    private static ProjectedEventField Project(FieldProjector field, object subject) =>
        new(field.Name, field.ProjectValue(subject));

    private static void ValidateFields(IReadOnlyList<FieldProjector> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fields[i].Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(fields[i].Path);
            ArgumentNullException.ThrowIfNull(fields[i].ProjectValue);
        }
    }

    private static void ValidateIncludes(IReadOnlyList<IncludeProjector> includes)
    {
        for (int i = 0; i < includes.Count; i++)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(includes[i].Name);
            ArgumentNullException.ThrowIfNull(includes[i].Project);
        }
    }

    private ValueTask<ProjectedEvent> ProjectWithIncludesAsync(
        object subject,
        TContext context,
        CancellationToken cancellationToken)
    {
        var includes = new ProjectedEventField[_includes.Length];
        for (int i = 0; i < _includes.Length; i++)
        {
            ValueTask<ProjectedEventField> projected =
                _includes[i].ProjectAsync(subject, context, cancellationToken);
            if (!projected.IsCompletedSuccessfully)
                return AwaitIncludesAsync(subject, context, includes, i, projected, cancellationToken);
            includes[i] = projected.Result;
        }

        return new ValueTask<ProjectedEvent>(ProjectFields(subject, includes));
    }

    private ValueTask<ReadOnlyMemory<byte>> ProjectPayloadWithIncludesAsync(
        object subject,
        TContext context,
        MessagePackSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var includes = new ProjectedEventField[_includes.Length];
        for (int i = 0; i < _includes.Length; i++)
        {
            ValueTask<ProjectedEventField> projected =
                _includes[i].ProjectAsync(subject, context, cancellationToken);
            if (!projected.IsCompletedSuccessfully)
                return AwaitPayloadIncludesAsync(subject, context, includes, i, projected, options, cancellationToken);
            includes[i] = projected.Result;
        }

        return new ValueTask<ReadOnlyMemory<byte>>(WritePayload(subject, includes, options));
    }

    private async ValueTask<ProjectedEvent> AwaitIncludesAsync(
        object subject,
        TContext context,
        ProjectedEventField[] includes,
        int start,
        ValueTask<ProjectedEventField> pending,
        CancellationToken cancellationToken)
    {
        includes[start] = await pending.ConfigureAwait(false);
        for (int i = start + 1; i < _includes.Length; i++)
        {
            includes[i] = await _includes[i].ProjectAsync(subject, context, cancellationToken)
                .ConfigureAwait(false);
        }

        return ProjectFields(subject, includes);
    }

    private async ValueTask<ReadOnlyMemory<byte>> AwaitPayloadIncludesAsync(
        object subject,
        TContext context,
        ProjectedEventField[] includes,
        int start,
        ValueTask<ProjectedEventField> pending,
        MessagePackSerializerOptions options,
        CancellationToken cancellationToken)
    {
        includes[start] = await pending.ConfigureAwait(false);
        for (int i = start + 1; i < _includes.Length; i++)
        {
            includes[i] = await _includes[i].ProjectAsync(subject, context, cancellationToken)
                .ConfigureAwait(false);
        }

        return WritePayload(subject, includes, options);
    }

    private ReadOnlyMemory<byte> WritePayload(
        object subject,
        IReadOnlyList<ProjectedEventField>? context,
        MessagePackSerializerOptions options)
    {
        IReadOnlyList<ProjectedEventField>? effectiveContext =
            context ?? (subject as ProjectedEvent)?.Context;
        Func<object, ProjectedEventField[]>? projectFields = Volatile.Read(ref _projectFields);
        if (projectFields is null)
        {
            return ProjectedPayloadWriter.Write(
                _metadata.EventType(subject),
                _metadata.EventName(subject),
                _fields,
                subject,
                effectiveContext,
                options);
        }

        return ProjectedPayloadWriter.Write(
            _metadata.EventType(subject),
            _metadata.EventName(subject),
            projectFields(subject),
            effectiveContext,
            options);
    }

    internal void PromoteProjectFields(Func<object, ProjectedEventField[]> projectFields)
    {
        ArgumentNullException.ThrowIfNull(projectFields);
        Volatile.Write(ref _projectFields, projectFields);
    }

    public sealed record FieldProjector(
        string Name,
        string Path,
        Func<object, ProjectedEventValue> ProjectValue)
    {
        internal ProjectedFieldPayloadWriter? WritePayload { get; init; }

        public ProjectedEventField Project(object subject) =>
            new(Name, ProjectValue(subject));
    }

    public sealed record IncludeProjector(
        string Name,
        Func<object, TContext, CancellationToken, ValueTask<ProjectedEventValue>> Project)
    {
        public ValueTask<ProjectedEventField> ProjectAsync(
            object subject,
            TContext context,
            CancellationToken cancellationToken)
        {
            ValueTask<ProjectedEventValue> projected = Project(subject, context, cancellationToken);
            return projected.IsCompletedSuccessfully
                ? new ValueTask<ProjectedEventField>(new ProjectedEventField(Name, projected.Result))
                : AwaitValueAsync(projected);
        }

        private async ValueTask<ProjectedEventField> AwaitValueAsync(
            ValueTask<ProjectedEventValue> projected) =>
            new(Name, await projected.ConfigureAwait(false));
    }
}
