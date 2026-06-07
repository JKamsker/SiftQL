namespace SiftQL.Examples.ShaRpc.SharedContracts.Domain;

public interface IServerRecord;

public interface IRegionalRecord : IServerRecord
{
    string Region { get; }
}
