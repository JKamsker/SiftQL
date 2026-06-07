namespace SiftQL.Schema;

public sealed class FilterArrayAccessor
{
    public FilterArrayAccessor(
        FilterScalarKind elementKind,
        Func<object, bool, bool>? booleanContains = null,
        Func<object, double, bool>? numberContains = null,
        Func<object, string?, bool>? textContains = null,
        Func<object, Guid, bool>? guidContains = null)
    {
        ElementKind = elementKind;
        BooleanContains = booleanContains;
        NumberContains = numberContains;
        TextContains = textContains;
        GuidContains = guidContains;
    }

    public FilterScalarKind ElementKind { get; }
    public Func<object, bool, bool>? BooleanContains { get; }
    public Func<object, double, bool>? NumberContains { get; }
    public Func<object, string?, bool>? TextContains { get; }
    public Func<object, Guid, bool>? GuidContains { get; }
}
