using SiftQL;
using SiftQL.Projected;

namespace SiftQL.Projection;

internal readonly struct ProjectionEventMetadata(Type eventMetadataType)
{
    private readonly string _eventType = eventMetadataType.FullName ?? eventMetadataType.Name;
    private readonly string _eventName = eventMetadataType.Name;
    private readonly bool _dynamic = eventMetadataType == typeof(ProjectedEvent) ||
        eventMetadataType.IsInterface ||
        eventMetadataType.IsAbstract;

    public string EventType(object subject) =>
        DynamicMetadata(subject) is { } projected
            ? projected.EventType
            : _dynamic
                ? subject.GetType().FullName ?? subject.GetType().Name
                : _eventType;

    public string EventName(object subject) =>
        DynamicMetadata(subject) is { } projected
            ? projected.EventName
            : _dynamic
                ? subject.GetType().Name
                : _eventName;

    public ProjectedEvent Create(
        object subject,
        ProjectedEventField[] fields,
        ProjectedEventField[]? context) =>
        new()
        {
            EventType = EventType(subject),
            EventName = EventName(subject),
            Fields = fields,
            Context = context ?? [],
        };

    private ProjectedEvent? DynamicMetadata(object subject) =>
        _dynamic && subject is ProjectedEvent projected ? projected : null;
}
