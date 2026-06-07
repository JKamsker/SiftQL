using SiftQL;

namespace SiftQL.Examples.ShaRpc.SharedContracts.Domain;

public static class KernelExtensions
{
    public static QueryKernel<TRecord> InRegion<TRecord>(
        this QueryKernel<TRecord> kernel,
        string region)
        where TRecord : IRegionalRecord
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        return kernel.Where(record => record.Region == region);
    }
}
