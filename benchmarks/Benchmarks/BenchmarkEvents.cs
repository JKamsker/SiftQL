using SiftQL;
using SiftQL.Projected;

namespace SiftQL.Benchmarks;

public sealed record SkillRef(int Id, string Name);

public sealed record ItemUsedEvent(
    Guid EventId,
    long CharacterId,
    int MapId,
    int ItemId,
    string ItemName,
    int Quantity) : IFilterSubject;

public sealed record DamageDealtEvent(
    Guid EventId,
    long CharacterId,
    long TargetId,
    int MapId,
    int Damage,
    string DamageType,
    bool Critical,
    SkillRef Skill) : IFilterSubject;

public sealed record UiSelectionChangedEvent(
    Guid EventId,
    long CharacterId,
    string ElementId,
    string SelectedValue) : IFilterSubject;

public sealed record ScalarArrayEvent(
    Guid EventId,
    long CharacterId,
    int MapId,
    int Damage,
    bool Accepted,
    SkillRef Skill,
    int[] SkillIds,
    string[] Tags) : IFilterSubject;

public sealed record LargeInEvent(Guid EventId, int Token) : IFilterSubject;

internal sealed record BenchmarkProjectionContext(ProjectedEventValue NearbyPlayers);
