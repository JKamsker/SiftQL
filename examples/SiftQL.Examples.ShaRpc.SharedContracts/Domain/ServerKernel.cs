using SiftQL;

namespace SiftQL.Examples.ShaRpc.SharedContracts.Domain;

[KernelCatalog(SubjectContract = typeof(IServerRecord))]
[KernelSubject(typeof(InventoryChangedEvent), Alias = "InventoryChanged")]
[KernelSubject(typeof(ServerOfferSnapshot), Alias = "ServerOffer")]
public static partial class ServerKernel;
