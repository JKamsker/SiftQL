namespace SiftQL.Compiler;

public class FilterValidationException : InvalidOperationException
{
    public FilterValidationException(string message)
        : base(message)
    {
    }
}
