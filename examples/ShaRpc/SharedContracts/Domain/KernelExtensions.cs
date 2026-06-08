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

    public static QueryKernel<TRecord, ServerLookupContext> WithServerContext<TRecord>(
        this QueryKernel<TRecord> kernel)
        where TRecord : IServerRecord
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return kernel.WithContext<TRecord, ServerLookupContext>();
    }
}
