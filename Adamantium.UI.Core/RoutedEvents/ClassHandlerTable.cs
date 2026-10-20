using System.Collections.Concurrent;

namespace Adamantium.UI.Core.RoutedEvents;

internal sealed class ClassHandlerTable
{
    private readonly ClassEventSubscription[] all;
    private readonly ConcurrentDictionary<Type, ClassEventSubscription[]> byType = new();

    public ClassHandlerTable(ClassEventSubscription[] all)
    {
        this.all = all;
    }

    public ClassEventSubscription[] All => all;

    public ClassEventSubscription[] For(Type type)
    {
        return byType.GetOrAdd(type, static (key, subscriptions) => Select(key, subscriptions), all);
    }

    private static ClassEventSubscription[] Select(Type type, ClassEventSubscription[] subscriptions)
    {
        List<ClassEventSubscription> selected = [];
        foreach (var subscription in subscriptions)
        {
            if (subscription.TargetType.IsAssignableFrom(type))
            {
                selected.Add(subscription);
            }
        }

        return [.. selected];
    }
}
