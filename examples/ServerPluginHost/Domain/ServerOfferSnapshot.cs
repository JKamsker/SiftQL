namespace SiftQL.Examples.ServerPluginHost.Domain;

public sealed record ServerOfferSnapshot(
    string Region,
    string OfferCode,
    string ItemCode,
    int Cost,
    int Stock,
    bool Enabled) : IRegionEvent;
