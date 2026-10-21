namespace Adamantium.UI.Core.RoutedEvents;

internal sealed class HandlerCall
{
    public HandlerCall(Type delegateType, Action<Delegate, object, RoutedEventArgs> invoke)
    {
        DelegateType = delegateType;
        Invoke = invoke;
    }

    public Type DelegateType { get; }

    public Action<Delegate, object, RoutedEventArgs> Invoke { get; }
}
