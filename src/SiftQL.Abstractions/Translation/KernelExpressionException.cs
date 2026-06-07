using SiftQL.Expressions;
using SiftQL.Projected;
namespace SiftQL.Translation;

public sealed class KernelExpressionException : InvalidOperationException
{
    public KernelExpressionException(string message)
        : base(message)
    {
    }
}
