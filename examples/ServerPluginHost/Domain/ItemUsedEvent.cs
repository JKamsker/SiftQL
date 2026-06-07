namespace SiftQL.Examples.ServerPluginHost.Domain;

public sealed record ItemUsedEvent(
    long AvatarId,
    string Region,
    string ItemCode,
    string ItemKind,
    int Quantity,
    int RemainingCharges) : IRegionEvent;
