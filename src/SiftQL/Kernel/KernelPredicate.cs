using System.Collections.Concurrent;
using System.Reflection;

namespace SiftQL.Kernel;

internal sealed class KernelPredicate
{
    private static readonly MethodInfo s_fromTypedCore =
        typeof(KernelPredicate).GetMethod(
            nameof(FromTypedCore),
            BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly ConcurrentDictionary<Type, Func<Delegate, KernelPredicate>> s_factories = new();

    public KernelPredicate(Func<object, bool> objectPredicate, Delegate? typedPredicate = null)
    {
        ObjectPredicate = objectPredicate ?? throw new ArgumentNullException(nameof(objectPredicate));
        TypedPredicate = typedPredicate;
    }

    public Func<object, bool> ObjectPredicate { get; }
    public Delegate? TypedPredicate { get; }

    public static KernelPredicate FromObject(Func<object, bool> predicate) =>
        new(predicate);

    public static KernelPredicate FromTypedDelegate(Type subjectType, Delegate predicate)
    {
        ArgumentNullException.ThrowIfNull(subjectType);
        ArgumentNullException.ThrowIfNull(predicate);
        return s_factories.GetOrAdd(subjectType, CreateFactory)(predicate);
    }

    private static Func<Delegate, KernelPredicate> CreateFactory(Type subjectType) =>
        s_fromTypedCore
            .MakeGenericMethod(subjectType)
            .CreateDelegate<Func<Delegate, KernelPredicate>>();

    private static KernelPredicate FromTypedCore<TSubject>(Delegate predicate)
    {
        var typed = (Func<TSubject, bool>)predicate;
        return new KernelPredicate(subject => typed((TSubject)subject), typed);
    }
}
