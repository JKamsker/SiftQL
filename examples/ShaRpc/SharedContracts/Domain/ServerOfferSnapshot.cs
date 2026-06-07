namespace SiftQL.Examples.ShaRpc.SharedContracts.Domain;

public sealed record ServerOfferSnapshot(
    string Region,
    string OfferCode,
    string ItemCode,
    int Cost,
    int Stock,
    bool Enabled) : IRegionalRecord;
