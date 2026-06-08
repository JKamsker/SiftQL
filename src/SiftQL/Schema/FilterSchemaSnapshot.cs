namespace SiftQL.Schema;

internal readonly record struct FilterSchemaSnapshot(FilterSchema Schema, int Version)
{
    public static FilterSchemaSnapshot For(Type subjectType)
    {
        while (true)
        {
            int before = FilterSchema.Version;
            FilterSchema schema = FilterSchema.For(subjectType);
            int after = FilterSchema.Version;
            if (before == after)
                return new FilterSchemaSnapshot(schema, after);
        }
    }
}
