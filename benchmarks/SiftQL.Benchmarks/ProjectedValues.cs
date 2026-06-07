using SiftQL;
using SiftQL.Projected;

namespace SiftQL.Benchmarks;

internal static class ProjectedValues
{
    public static ProjectedEventValue Boolean(bool value) =>
        new() { Kind = ProjectedEventValueKind.Boolean, Boolean = value };

    public static ProjectedEventValue Integer(long value) =>
        new() { Kind = ProjectedEventValueKind.Integer, Integer = value };

    public static ProjectedEventValue String(string value) =>
        new() { Kind = ProjectedEventValueKind.String, String = value };

    public static ProjectedEventValue Guid(Guid value) =>
        new() { Kind = ProjectedEventValueKind.Guid, Guid = value };

    public static ProjectedEventField Field(string name, ProjectedEventValue value) =>
        new(name, value);

    public static ProjectedEventValue NearbyPlayers() =>
        ProjectedEventValue.FromValues(
        [
            Player(101, "Alpha", 4),
            Player(102, "Beta", 8),
            Player(103, "Gamma", 12),
        ]);

    private static ProjectedEventValue Player(long id, string name, long distance) =>
        ProjectedEventValue.FromFields(
        [
            Field("CharacterId", Integer(id)),
            Field("Name", String(name)),
            Field("Distance", Integer(distance)),
        ]);
}
