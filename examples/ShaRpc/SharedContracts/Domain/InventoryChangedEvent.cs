namespace SiftQL.Examples.ShaRpc.SharedContracts.Domain;

public sealed record InventoryChangedEvent(
    long SessionId,
    string Region,
    string ItemCode,
    int Quantity,
    int Capacity) : IRegionalRecord;
