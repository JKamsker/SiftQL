namespace SiftQL.Examples.ServerPluginHost.Domain;

public sealed record ZoneEnteredEvent(
    long AvatarId,
    string Region,
    string ZoneCode,
    bool FirstVisit) : IRegionEvent;
