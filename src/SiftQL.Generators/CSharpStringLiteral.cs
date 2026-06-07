using System.Text;

namespace SiftQL.Generators;

internal static class CSharpStringLiteral
{
    public static void AppendTo(StringBuilder source, string value)
    {
        source.Append('"');
        foreach (char ch in value)
        {
            switch (ch)
            {
                case '\\':
                    source.Append("\\\\");
                    break;
                case '"':
                    source.Append("\\\"");
                    break;
                case '\r':
                    source.Append("\\r");
                    break;
                case '\n':
                    source.Append("\\n");
                    break;
                case '\t':
                    source.Append("\\t");
                    break;
                case '\u2028':
                    source.Append("\\u2028");
                    break;
                case '\u2029':
                    source.Append("\\u2029");
                    break;
                default:
                    if (char.IsControl(ch))
                        source.Append("\\u").Append(((int)ch).ToString("x4"));
                    else
                        source.Append(ch);
                    break;
            }
        }

        source.Append('"');
    }
}
