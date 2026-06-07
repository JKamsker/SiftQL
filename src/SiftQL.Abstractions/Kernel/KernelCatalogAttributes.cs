namespace SiftQL;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class KernelCatalogAttribute : Attribute
{
    public Type? SubjectContract { get; set; } = typeof(IFilterSubject);
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class KernelSubjectAttribute(Type subjectType) : Attribute
{
    public Type SubjectType { get; } = subjectType;

    public string? Alias { get; set; }
}
