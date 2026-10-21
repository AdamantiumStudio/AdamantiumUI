namespace Adamantium.UI.Core.RoutedEvents;

internal sealed class HandlerCall
{
    public HandlerCall(Type delegateType, Action<Delegate, object, RoutedEventArgs> invoke, bool isDynamic)
    {
        DelegateType = delegateType;
        Invoke = invoke;
        IsDynamic = isDynamic;
    }

    public Type DelegateType { get; }

    public Action<Delegate, object, RoutedEventArgs> Invoke { get; }

    public bool IsDynamic { get; }
}
