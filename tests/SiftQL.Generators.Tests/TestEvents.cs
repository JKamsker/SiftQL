using SiftQL;

namespace SiftQL.Generators.Tests;

internal sealed record SkillRef(int Id, int Level);

internal sealed record ItemUsedEvent(
    Guid EventId,
    long CharacterId,
    int ItemId,
    int Quantity) : IFilterSubject;
