using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using SiftQL.Expressions;
using SiftQL.Projected;

namespace SiftQL.Translation;

internal sealed class ContextExpressionIncludes
{
    private readonly ParameterExpression _subject;
    private readonly ParameterExpression _context;
    private Dictionary<string, EventProjectionInclude> _known = [];
    private readonly List<EventProjectionInclude> _newIncludes = [];
    private readonly List<ContextProjectionBinding> _bindings = [];
    private readonly Func<string> _nextParameterKey;
    private int _parameterIndex;

    public ContextExpressionIncludes(
        ParameterExpression subject,
        ParameterExpression context,
        IReadOnlyList<ContextProjectionBinding> bindings,
        int parameterOffset)
    {
        _subject = subject;
        _context = context;
        _parameterIndex = parameterOffset;
        _nextParameterKey = NextLocalParameterKey;
        LoadBindings(bindings);
    }

    public ContextExpressionIncludes(
        ParameterExpression subject,
        ParameterExpression context,
        IReadOnlyList<ContextProjectionBinding> bindings,
        Func<string> nextParameterKey)
    {
        _subject = subject;
        _context = context;
        _nextParameterKey = nextParameterKey ?? throw new ArgumentNullException(nameof(nextParameterKey));
        LoadBindings(bindings);
    }

    private void LoadBindings(IReadOnlyList<ContextProjectionBinding> bindings)
    {
        _known = bindings.ToDictionary(
            static item => item.Key,
            static item => item.Include,
            StringComparer.Ordinal);
        _bindings.AddRange(bindings);
    }

    public EventProjectionInclude[] NewIncludes => _newIncludes.ToArray();
    public ContextProjectionBinding[] Bindings => _bindings.ToArray();

    public bool TryTranslate(Expression expression, string? name, out string projectedPath)
    {
        expression = StripConvert(expression);
        if (!TrySplitContextExpression(expression, out MethodCallExpression? call, out string memberPath))
        {
            projectedPath = string.Empty;
            return false;
        }

        EventProjectionArgument[] arguments = TranslateArguments(call);
        string intrinsic = EventProjectionContextIntrinsics.Method(call.Method.Name, memberPath);
        string key = IncludeKey(intrinsic, arguments);
        if (!_known.TryGetValue(key, out EventProjectionInclude? include))
        {
            include = new EventProjectionInclude(
                intrinsic,
                ContextResultName(name),
                arguments);
            _known.Add(key, include);
            _newIncludes.Add(include);
            _bindings.Add(new ContextProjectionBinding(key, include));
        }

        projectedPath = ProjectedEventPaths.Context(include.ResultName);
        return true;
    }

    private EventProjectionArgument[] TranslateArguments(MethodCallExpression call)
    {
        ParameterInfo[] parameters = call.Method.GetParameters();
        var arguments = new EventProjectionArgument[call.Arguments.Count];
        for (int i = 0; i < call.Arguments.Count; i++)
        {
            string name = string.IsNullOrWhiteSpace(parameters[i].Name)
                ? "arg" + i
                : parameters[i].Name!;
            Expression argument = StripConvert(call.Arguments[i]);
            arguments[i] = TryGetSubjectFieldPath(argument, out string? fieldPath)
                ? EventProjectionArgument.FromSourceField(name, fieldPath)
                : new EventProjectionArgument(
                    name,
                    KernelExpressionEvaluator.EvaluateValue(
                        argument,
                        _subject,
                        _context,
                        _nextParameterKey()));
        }

        return arguments;
    }

    private bool TrySplitContextExpression(
        Expression expression,
        out MethodCallExpression call,
        out string memberPath)
    {
        var members = new Stack<string>();
        Expression current = StripConvert(expression);
        while (current is MemberExpression member)
        {
            members.Push(member.Member.Name);
            current = StripConvert(member.Expression!);
        }

        if (current is MethodCallExpression directCall &&
            directCall.Object is not null &&
            StripConvert(directCall.Object) == _context)
        {
            call = directCall;
            memberPath = string.Join(".", members);
            return true;
        }

        call = null!;
        memberPath = string.Empty;
        return false;
    }

    private bool TryGetSubjectFieldPath(Expression expression, out string fieldPath)
    {
        expression = StripConvert(expression);
        var names = new Stack<string>();
        Expression? current = expression;
        while (current is MemberExpression member)
        {
            names.Push(member.Member.Name);
            current = StripConvert(member.Expression!);
        }

        if (current == _subject && names.Count > 0)
        {
            fieldPath = string.Join(".", names);
            return true;
        }

        fieldPath = string.Empty;
        return false;
    }

    private string ContextResultName(string? preferredName) =>
        string.IsNullOrWhiteSpace(preferredName)
            ? "__ctx" + _bindings.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : preferredName;

    private static string IncludeKey(string intrinsic, IReadOnlyList<EventProjectionArgument> arguments)
    {
        var builder = new StringBuilder(intrinsic);
        for (int i = 0; i < arguments.Count; i++)
        {
            builder.Append('|').Append(arguments[i].Name).Append(':').Append((int)arguments[i].Kind);
            if (arguments[i].Kind == EventProjectionArgumentKind.SourceField)
                builder.Append(':').Append(arguments[i].SourcePath);
            else
                AppendLiteral(builder, arguments[i].Value);
        }

        return builder.ToString();
    }

    private static void AppendLiteral(StringBuilder builder, FilterValue value) =>
        builder
            .Append(':')
            .Append((int)value.Kind)
            .Append(':')
            .Append(value.Boolean)
            .Append(':')
            .Append(value.Integer)
            .Append(':')
            .Append(value.UnsignedInteger)
            .Append(':')
            .Append(BitConverter.DoubleToInt64Bits(value.Number))
            .Append(':')
            .Append(value.Decimal)
            .Append(':')
            .Append(value.String)
            .Append(':')
            .Append(value.Guid);

    private string NextLocalParameterKey() =>
        "p" + _parameterIndex++;

    private static Expression StripConvert(Expression expression)
    {
        while (expression.NodeType is
            ExpressionType.Convert or
            ExpressionType.ConvertChecked or
            ExpressionType.TypeAs)
        {
            expression = ((UnaryExpression)expression).Operand;
        }

        return expression;
    }
}
