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
    private static readonly ConcurrentDictionary<Type, Action<Delegate, object, RoutedEventArgs>> Invokers = new();

    /// <summary>Registers the call for handlers of <paramref name="delegateType"/>. Called from generated module
    /// initializers; the first registration of a type wins.</summary>
    public static void Register(Type delegateType, Action<Delegate, object, RoutedEventArgs> invoke)
    {
        ArgumentNullException.ThrowIfNull(delegateType);
        ArgumentNullException.ThrowIfNull(invoke);

        Invokers.TryAdd(delegateType, invoke);
    }

    internal static Action<Delegate, object, RoutedEventArgs> For(Type delegateType)
    {
        if (Invokers.TryGetValue(delegateType, out var invoke))
        {
            return invoke;
        }

        RuntimeHelpers.RunModuleConstructor(delegateType.Module.ModuleHandle);
        return Invokers.TryGetValue(delegateType, out invoke) ? invoke : Dynamic;
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
