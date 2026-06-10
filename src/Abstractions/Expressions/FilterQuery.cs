using System.Globalization;
using System.Text;

namespace SiftQL.Expressions;

// A small, schema-less text DSL so non-.NET clients can author filters without
// hand-writing integer-discriminator JSON. Grammar (lowest to highest binding):
//
//   or    := and (("or" | "||") and)*
//   and   := not (("and" | "&&") not)*
//   not   := ("not" | "!") not | primary
//   primary := "(" or ")" | "true" | "false" | comparison
//   comparison := field op value
//   op    := == | != | > | >= | < | <= | contains | startswith | endswith | in | between
//             optionally followed by "~" for case-insensitive string matching (e.g. name ==~ "x")
//   value := string | number["m"] | true | false | null | guid "..."
//             (in/between take "[" value,... "]"; the "m" suffix marks a decimal)
//
// Format() is the inverse for the kinds the DSL covers (Compare/In/Between/And/Or/Not/Any).
public static class FilterQuery
{
    public static FilterExpression Parse(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tokens = Tokenizer.Tokenize(query);
        var parser = new Parser(tokens, query);
        FilterExpression filter = parser.ParseExpression();
        parser.ExpectEnd();
        return filter;
    }

    public static string Format(FilterExpression filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var builder = new StringBuilder();
        Formatter.Append(builder, filter);
        return builder.ToString();
    }

    private enum TokenKind
    {
        Identifier,
        String,
        Number,
        Symbol,
        LeftParen,
        RightParen,
        LeftBracket,
        RightBracket,
        Comma,
        End,
    }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static class Tokenizer
    {
        public static List<Token> Tokenize(string input)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < input.Length)
            {
                char c = input[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                    continue;
                }

                int start = i;
                switch (c)
                {
                    case '(': tokens.Add(new Token(TokenKind.LeftParen, "(", start)); i++; continue;
                    case ')': tokens.Add(new Token(TokenKind.RightParen, ")", start)); i++; continue;
                    case '[': tokens.Add(new Token(TokenKind.LeftBracket, "[", start)); i++; continue;
                    case ']': tokens.Add(new Token(TokenKind.RightBracket, "]", start)); i++; continue;
                    case ',': tokens.Add(new Token(TokenKind.Comma, ",", start)); i++; continue;
                    case '~': tokens.Add(new Token(TokenKind.Symbol, "~", start)); i++; continue;
                    case '"': tokens.Add(ReadString(input, ref i)); continue;
                }

                if (c is '=' or '!' or '<' or '>' or '|' or '&')
                {
                    tokens.Add(ReadSymbol(input, ref i));
                    continue;
                }

                if (char.IsDigit(c) || (c == '-' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
                {
                    tokens.Add(ReadNumber(input, ref i));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    tokens.Add(ReadIdentifier(input, ref i));
                    continue;
                }

                throw new FilterQueryException($"Unexpected character '{c}'.", start);
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, input.Length));
            return tokens;
        }

        private static Token ReadString(string input, ref int i)
        {
            int start = i;
            i++; // opening quote
            var builder = new StringBuilder();
            while (i < input.Length && input[i] != '"')
            {
                char c = input[i];
                if (c == '\\' && i + 1 < input.Length)
                {
                    i++;
                    builder.Append(input[i] switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\\' => '\\',
                        var other => other,
                    });
                }
                else
                {
                    builder.Append(c);
                }

                i++;
            }

            if (i >= input.Length)
                throw new FilterQueryException("Unterminated string literal.", start);

            i++; // closing quote
            return new Token(TokenKind.String, builder.ToString(), start);
        }

        private static Token ReadSymbol(string input, ref int i)
        {
            int start = i;
            char c = input[i];
            char? next = i + 1 < input.Length ? input[i + 1] : null;
            string symbol = (c, next) switch
            {
                ('=', '=') => "==",
                ('!', '=') => "!=",
                ('<', '=') => "<=",
                ('>', '=') => ">=",
                ('|', '|') => "||",
                ('&', '&') => "&&",
                ('<', _) => "<",
                ('>', _) => ">",
                _ => throw new FilterQueryException($"Unexpected operator '{c}'.", start),
            };
            i += symbol.Length;
            return new Token(TokenKind.Symbol, symbol, start);
        }

        private static Token ReadNumber(string input, ref int i)
        {
            int start = i;
            if (input[i] == '-')
                i++;
            while (i < input.Length && (char.IsDigit(input[i]) || input[i] is '.' or 'e' or 'E' or '+' or '-'))
                i++;
            // Optional decimal type suffix emitted by Format() so the Decimal value
            // kind survives a Format -> Parse round-trip.
            if (i < input.Length && input[i] is 'm' or 'M')
                i++;
            return new Token(TokenKind.Number, input[start..i], start);
        }

        private static Token ReadIdentifier(string input, ref int i)
        {
            int start = i;
            while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] is '_' or '.'))
                i++;
            return new Token(TokenKind.Identifier, input[start..i], start);
        }
    }

    private sealed class Parser(List<Token> tokens, string source)
    {
        private int _index;

        private Token Current => tokens[_index];

        public FilterExpression ParseExpression() => ParseOr();

        public void ExpectEnd()
        {
            if (Current.Kind != TokenKind.End)
                throw Error($"Unexpected '{Current.Text}'.");
        }

        private FilterExpression ParseOr()
        {
            var children = new List<FilterExpression> { ParseAnd() };
            while (IsKeyword("or") || (Current.Kind == TokenKind.Symbol && Current.Text == "||"))
            {
                _index++;
                children.Add(ParseAnd());
            }

            return children.Count == 1 ? children[0] : FilterExpression.Or([.. children]);
        }

        private FilterExpression ParseAnd()
        {
            var children = new List<FilterExpression> { ParseNot() };
            while (IsKeyword("and") || (Current.Kind == TokenKind.Symbol && Current.Text == "&&"))
            {
                _index++;
                children.Add(ParseNot());
            }

            return children.Count == 1 ? children[0] : FilterExpression.And([.. children]);
        }

        private FilterExpression ParseNot()
        {
            if (IsKeyword("not") || (Current.Kind == TokenKind.Symbol && Current.Text == "!"))
            {
                _index++;
                return FilterExpression.Not(ParseNot());
            }

            return ParsePrimary();
        }

        private FilterExpression ParsePrimary()
        {
            if (Current.Kind == TokenKind.LeftParen)
            {
                _index++;
                FilterExpression inner = ParseOr();
                Expect(TokenKind.RightParen, ")");
                return inner;
            }

            if (Current.Kind == TokenKind.Identifier && IsKeyword("true"))
            {
                _index++;
                return FilterExpression.Any;
            }

            if (Current.Kind == TokenKind.Identifier && IsKeyword("false"))
            {
                _index++;
                return FilterExpression.Not(FilterExpression.Any);
            }

            return ParseComparison();
        }

        private FilterExpression ParseComparison()
        {
            if (Current.Kind != TokenKind.Identifier)
                throw Error($"Expected a field name but found '{Current.Text}'.");

            string field = Current.Text;
            _index++;

            string op = ReadOperator();
            bool ignoreCase = false;
            if (Current.Kind == TokenKind.Symbol && Current.Text == "~")
            {
                Token marker = Current;
                ignoreCase = true;
                _index++;
                if (!SupportsIgnoreCase(op))
                {
                    throw new FilterQueryException(
                        $"Case-insensitive marker '~' is not valid for operator '{op}'.",
                        marker.Position);
                }
            }

            if (op == "in")
                return FilterExpression.In(field, ReadValueList());
            if (op == "between")
            {
                IReadOnlyList<FilterValue> bounds = ReadValueList();
                if (bounds.Count != 2)
                    throw Error("between requires exactly two values: field between [lower, upper].");
                return FilterExpression.Between(field, bounds[0], bounds[1]);
            }

            FilterValue value = ReadValue();
            return op switch
            {
                "==" => FilterExpression.Compare(field, FilterOperator.Equal, value, ignoreCase),
                "!=" => FilterExpression.Compare(field, FilterOperator.NotEqual, value, ignoreCase),
                ">" => FilterExpression.Compare(field, FilterOperator.GreaterThan, value, ignoreCase),
                ">=" => FilterExpression.Compare(field, FilterOperator.GreaterThanOrEqual, value, ignoreCase),
                "<" => FilterExpression.Compare(field, FilterOperator.LessThan, value, ignoreCase),
                "<=" => FilterExpression.Compare(field, FilterOperator.LessThanOrEqual, value, ignoreCase),
                "contains" => FilterExpression.StringContains(field, value, ignoreCase),
                "startswith" => FilterExpression.StringStartsWith(field, value, ignoreCase),
                "endswith" => FilterExpression.StringEndsWith(field, value, ignoreCase),
                _ => throw Error($"Unknown operator '{op}'."),
            };
        }

        private static bool SupportsIgnoreCase(string op) =>
            op is "==" or "!=" or "contains" or "startswith" or "endswith";

        private string ReadOperator()
        {
            if (Current.Kind == TokenKind.Symbol)
            {
                string symbol = Current.Text;
                _index++;
                return symbol;
            }

            if (Current.Kind == TokenKind.Identifier &&
                Current.Text.ToLowerInvariant() is "in" or "between" or "contains" or "startswith" or "endswith")
            {
                string keyword = Current.Text.ToLowerInvariant();
                _index++;
                return keyword;
            }

            throw Error($"Expected an operator but found '{Current.Text}'.");
        }

        private IReadOnlyList<FilterValue> ReadValueList()
        {
            Expect(TokenKind.LeftBracket, "[");
            var values = new List<FilterValue>();
            if (Current.Kind != TokenKind.RightBracket)
            {
                values.Add(ReadValue());
                while (Current.Kind == TokenKind.Comma)
                {
                    _index++;
                    values.Add(ReadValue());
                }
            }

            Expect(TokenKind.RightBracket, "]");
            if (values.Count == 0)
                throw Error("In lists require at least one value.");
            return values;
        }

        private FilterValue ReadValue()
        {
            Token token = Current;
            switch (token.Kind)
            {
                case TokenKind.String:
                    _index++;
                    return FilterValue.From(token.Text);
                case TokenKind.Number:
                    _index++;
                    return ParseNumber(token);
                case TokenKind.Identifier when IsKeywordText(token.Text, "guid"):
                    _index++;
                    return ReadGuidLiteral();
                case TokenKind.Identifier when IsKeywordText(token.Text, "true"):
                    _index++;
                    return FilterValue.From(true);
                case TokenKind.Identifier when IsKeywordText(token.Text, "false"):
                    _index++;
                    return FilterValue.From(false);
                case TokenKind.Identifier when IsKeywordText(token.Text, "null"):
                    _index++;
                    return FilterValue.Null;
                default:
                    throw Error($"Expected a value but found '{token.Text}'.");
            }
        }

        private FilterValue ReadGuidLiteral()
        {
            Token token = Current;
            if (token.Kind != TokenKind.String)
                throw Error($"Expected a quoted guid value but found '{token.Text}'.");
            _index++;
            if (!Guid.TryParse(token.Text, out Guid guid))
                throw Error($"Invalid guid literal '{token.Text}'.");
            return FilterValue.From(guid);
        }

        private FilterValue ParseNumber(Token token)
        {
            string text = token.Text;

            // Decimal suffix emitted by Format() preserves the Decimal value kind.
            if (text.Length > 0 && text[^1] is 'm' or 'M')
            {
                string numeric = text[..^1];
                if (decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal dec))
                    return FilterValue.From(dec);
                throw Error($"Invalid number '{token.Text}'.");
            }

            if (!text.Contains('.') && !text.Contains('e') && !text.Contains('E'))
            {
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long integer))
                    return FilterValue.From(integer);
                // Integral values beyond long.MaxValue round-trip as UnsignedInteger.
                if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong unsigned))
                    return FilterValue.From(unsigned);
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return FilterValue.From(number);

            throw Error($"Invalid number '{token.Text}'.");
        }

        private void Expect(TokenKind kind, string text)
        {
            if (Current.Kind != kind)
                throw Error($"Expected '{text}' but found '{Current.Text}'.");
            _index++;
        }

        private bool IsKeyword(string keyword) =>
            Current.Kind == TokenKind.Identifier && IsKeywordText(Current.Text, keyword);

        private static bool IsKeywordText(string text, string keyword) =>
            string.Equals(text, keyword, StringComparison.OrdinalIgnoreCase);

        private FilterQueryException Error(string message) =>
            new($"{message} (at position {Current.Position} in '{source}')", Current.Position);
    }

    private static class Formatter
    {
        public static void Append(StringBuilder builder, FilterExpression filter)
        {
            switch (filter.Kind)
            {
                case FilterExpressionKind.Any:
                    builder.Append("true");
                    break;
                case FilterExpressionKind.And:
                    AppendComposite(builder, filter, "and");
                    break;
                case FilterExpressionKind.Or:
                    AppendComposite(builder, filter, "or");
                    break;
                case FilterExpressionKind.Not:
                    builder.Append("not ");
                    AppendGrouped(builder, filter.Children[0]);
                    break;
                case FilterExpressionKind.Compare:
                    AppendCompare(builder, filter);
                    break;
                case FilterExpressionKind.In:
                    AppendValueList(builder, filter, "in");
                    break;
                case FilterExpressionKind.Between:
                    AppendValueList(builder, filter, "between");
                    break;
                default:
                    throw new FilterQueryException(
                        $"Filter kind '{filter.Kind}' has no text-query representation.", 0);
            }
        }

        private static void AppendComposite(StringBuilder builder, FilterExpression filter, string keyword)
        {
            builder.Append('(');
            for (int i = 0; i < filter.Children.Length; i++)
            {
                if (i > 0)
                    builder.Append(' ').Append(keyword).Append(' ');
                Append(builder, filter.Children[i]);
            }

            builder.Append(')');
        }

        private static void AppendGrouped(StringBuilder builder, FilterExpression filter)
        {
            if (filter.Kind is FilterExpressionKind.And or FilterExpressionKind.Or)
            {
                Append(builder, filter);
                return;
            }

            builder.Append('(');
            Append(builder, filter);
            builder.Append(')');
        }

        private static void AppendCompare(StringBuilder builder, FilterExpression filter)
        {
            builder.Append(filter.Field).Append(' ').Append(filter.Operator switch
            {
                FilterOperator.Equal => "==",
                FilterOperator.NotEqual => "!=",
                FilterOperator.GreaterThan => ">",
                FilterOperator.GreaterThanOrEqual => ">=",
                FilterOperator.LessThan => "<",
                FilterOperator.LessThanOrEqual => "<=",
                FilterOperator.StringContains => "contains",
                FilterOperator.StringStartsWith => "startswith",
                FilterOperator.StringEndsWith => "endswith",
                _ => throw new FilterQueryException($"Operator '{filter.Operator}' is not representable.", 0),
            });
            if (filter.IgnoreCase)
                builder.Append('~');
            builder.Append(' ');
            AppendValue(builder, filter.Value);
        }

        private static void AppendValueList(StringBuilder builder, FilterExpression filter, string keyword)
        {
            builder.Append(filter.Field).Append(' ').Append(keyword).Append(" [");
            for (int i = 0; i < filter.Values.Length; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                AppendValue(builder, filter.Values[i]);
            }

            builder.Append(']');
        }

        private static void AppendValue(StringBuilder builder, FilterValue? value)
        {
            if (value is null)
            {
                builder.Append("null");
                return;
            }

            switch (value.Kind)
            {
                case FilterValueKind.Null:
                    builder.Append("null");
                    break;
                case FilterValueKind.Boolean:
                    builder.Append(value.Boolean ? "true" : "false");
                    break;
                case FilterValueKind.Integer:
                    builder.Append(value.Integer.ToString(CultureInfo.InvariantCulture));
                    break;
                case FilterValueKind.UnsignedInteger:
                    builder.Append(value.UnsignedInteger.ToString(CultureInfo.InvariantCulture));
                    break;
                case FilterValueKind.Number:
                    builder.Append(value.Number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case FilterValueKind.Decimal:
                    builder.Append(value.Decimal.ToString(CultureInfo.InvariantCulture)).Append('m');
                    break;
                case FilterValueKind.Guid:
                    builder.Append("guid \"").Append(value.Guid.ToString("D")).Append('"');
                    break;
                case FilterValueKind.String:
                    AppendString(builder, value.String ?? string.Empty);
                    break;
                default:
                    throw new FilterQueryException(
                        $"Filter value kind '{value.Kind}' has no text-query representation.",
                        0);
            }
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(c); break;
                }
            }

            builder.Append('"');
        }
    }
}

public sealed class FilterQueryException : FormatException
{
    public FilterQueryException(string message, int position)
        : base(message) =>
        Position = position;

    public int Position { get; }
}
