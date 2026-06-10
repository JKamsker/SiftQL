using System.Globalization;
using System.Text;

namespace SiftQL.Generators.Hot;

internal static class HotFilterValueEmitter
{
    public static void Emit(StringBuilder source, HotFilterValue value)
    {
        source.Append("new FilterValue { Kind = (FilterValueKind)");
        source.Append(((int)value.Kind).ToString(CultureInfo.InvariantCulture));
        if (value.Kind == HotFilterValueKind.Boolean)
            source.Append(", Boolean = ").Append(value.Boolean ? "true" : "false");
        if (value.Kind == HotFilterValueKind.Integer)
            source.Append(", Integer = ").Append(value.Integer.ToString(CultureInfo.InvariantCulture)).Append("L");
        if (value.Kind == HotFilterValueKind.UnsignedInteger)
            source.Append(", UnsignedInteger = ").Append(value.UnsignedInteger.ToString(CultureInfo.InvariantCulture)).Append("UL");
        if (value.Kind == HotFilterValueKind.Number)
            source.Append(", Number = ").Append(value.Number.ToString("R", CultureInfo.InvariantCulture)).Append("D");
        if (value.Kind == HotFilterValueKind.Decimal)
            source.Append(", Decimal = ").Append(value.Decimal.ToString(CultureInfo.InvariantCulture)).Append("M");
        if (value.Kind == HotFilterValueKind.String)
            EmitString(source, value.String);
        if (value.Kind == HotFilterValueKind.Guid)
        {
            source.Append(", Guid = new Guid(");
            CSharpStringLiteral.AppendTo(source, value.Guid);
            source.Append(")");
        }
        if (value.Kind == HotFilterValueKind.Timestamp)
        {
            source.Append(", Timestamp = new DateTimeOffset(")
                .Append(value.TimestampTicks.ToString(CultureInfo.InvariantCulture))
                .Append("L, TimeSpan.Zero)");
        }
        source.Append(" }");
    }

    private static void EmitString(StringBuilder source, string? value)
    {
        source.Append(", String = ");
        if (value is null)
            source.Append("null");
        else
            CSharpStringLiteral.AppendTo(source, value);
    }
}
