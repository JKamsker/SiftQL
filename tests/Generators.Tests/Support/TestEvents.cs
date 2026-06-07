using SiftQL;

namespace SiftQL.Generators.Tests;

public sealed record SkillRef(int Id, int Level);

public sealed record ItemUsedEvent(
    Guid EventId,
    long CharacterId,
    int ItemId,
    int Quantity) : IFilterSubject;
