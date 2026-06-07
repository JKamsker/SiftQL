using System.Reflection;

namespace SiftQL.Schema;

public delegate bool GeneratedFilterSchemaProviderDelegate(Type subjectType, out FilterSchema? schema);

public static class GeneratedFilterSchemaRegistry
{
    public static FilterSchema Create(Type subjectType, IReadOnlyList<FilterField> fields)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(fields);
        return new FilterSchema(subjectType, fields);
    }

    public static void Register(Assembly assembly, GeneratedFilterSchemaProviderDelegate provider)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(provider);
        FilterSchema.RegisterGeneratedProvider(assembly, provider);
    }
}
