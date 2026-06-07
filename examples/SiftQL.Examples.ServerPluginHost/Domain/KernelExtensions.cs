using SiftQL;

namespace SiftQL.Examples.ServerPluginHost.Domain;

public static class KernelExtensions
{
    public static QueryKernel<ItemUsedEvent> Consumable(this QueryKernel<ItemUsedEvent> kernel)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        return kernel.Where(static ev => ev.ItemKind == "consumable");
    }

    public static QueryKernel<TEvent> InRegion<TEvent>(
        this QueryKernel<TEvent> kernel,
        string region)
        where TEvent : IRegionEvent
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentException.ThrowIfNullOrWhiteSpace(region);
        return kernel.Where(ev => ev.Region == region);
    }
}
