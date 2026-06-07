using SiftQL;

namespace SiftQL.Examples.ServerPluginHost.Domain;

public interface IRegionEvent : IFilterSubject
{
    string Region { get; }
}
