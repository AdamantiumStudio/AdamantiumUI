using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Adamantium.UI.Core.RoutedEvents;

/// <summary>How a routed event calls a handler of each delegate type: a direct call the UI generator writes for every
/// handler delegate declared in an assembly it runs in, so nothing is reflected or generated at run time (iOS,
/// NativeAOT). A delegate type nothing registered is called through <see cref="Delegate.DynamicInvoke"/>.</summary>
public static class RoutedEventInvokers
{
    private static readonly ConcurrentDictionary<Type, HandlerCall> Calls = new();
    private static readonly Action<Delegate, object, RoutedEventArgs> DynamicCall = Dynamic;

    /// <summary>Registers the call for handlers of <paramref name="delegateType"/>. Called from generated module
    /// initializers; the first registration of a type wins.</summary>
    public static void Register(Type delegateType, Action<Delegate, object, RoutedEventArgs> invoke)
    {
        ArgumentNullException.ThrowIfNull(delegateType);
        ArgumentNullException.ThrowIfNull(invoke);

        Calls.AddOrUpdate(delegateType, static (type, call) => new HandlerCall(type, call),
            static (type, known, call) => known.Invoke == DynamicCall ? new HandlerCall(type, call) : known, invoke);
    }

    internal static HandlerCall For(Type delegateType)
    {
        if (Calls.TryGetValue(delegateType, out var call))
        {
            return call;
        }

        RuntimeHelpers.RunModuleConstructor(delegateType.Module.ModuleHandle);
        return Calls.GetOrAdd(delegateType, static type => new HandlerCall(type, DynamicCall));
    }

    private static void Dynamic(Delegate handler, object sender, RoutedEventArgs e)
    {
        try
        {
            handler.DynamicInvoke(sender, e);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException != null)
        {
            ExceptionDispatchInfo.Throw(wrapped.InnerException);
        }
    }
}
