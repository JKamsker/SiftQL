namespace SiftQL.Examples.ServerPluginHost.Domain;

public sealed record AbilityCastEvent(
    long AvatarId,
    string Region,
    string AbilityCode,
    int Power,
    bool Critical) : IRegionEvent;
