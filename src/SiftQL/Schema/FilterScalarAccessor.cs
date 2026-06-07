namespace SiftQL.Schema;

public sealed class FilterScalarAccessor
{
    public FilterScalarAccessor(
        FilterScalarKind kind,
        Func<object, bool?>? boolean = null,
        Func<object, double?>? number = null,
        Func<object, string?>? text = null,
        Func<object, Guid?>? guid = null,
        Func<object, long?>? enumeration = null,
        Func<object, bool>? requiredBoolean = null,
        Func<object, double>? requiredNumber = null,
        Func<object, Guid>? requiredGuid = null,
        Func<object, long>? requiredEnumeration = null)
    {
        Kind = kind;
        Boolean = boolean ?? (requiredBoolean is null ? null : subject => requiredBoolean(subject));
        Number = number ?? (requiredNumber is null ? null : subject => requiredNumber(subject));
        Text = text;
        Guid = guid ?? (requiredGuid is null ? null : subject => requiredGuid(subject));
        Enumeration = enumeration ?? (requiredEnumeration is null ? null : subject => requiredEnumeration(subject));
        RequiredBoolean = requiredBoolean;
        RequiredNumber = requiredNumber;
        RequiredGuid = requiredGuid;
        RequiredEnumeration = requiredEnumeration;
    }

    public FilterScalarKind Kind { get; }
    public Func<object, bool?>? Boolean { get; }
    public Func<object, double?>? Number { get; }
    public Func<object, string?>? Text { get; }
    public Func<object, Guid?>? Guid { get; }
    public Func<object, long?>? Enumeration { get; }
    public Func<object, bool>? RequiredBoolean { get; }
    public Func<object, double>? RequiredNumber { get; }
    public Func<object, Guid>? RequiredGuid { get; }
    public Func<object, long>? RequiredEnumeration { get; }
}
