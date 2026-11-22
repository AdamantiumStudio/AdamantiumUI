using Adamantium.Core.Collections;
using Adamantium.UI.Core.Behaviors;

namespace Adamantium.UI.Core.Collections;

public class BehaviorCollection : TrackingCollection<Behavior>
{
    private IAdamantiumComponent _adamantiumComponent;
    public BehaviorCollection(IAdamantiumComponent adamantiumComponent)
    {
        _adamantiumComponent = adamantiumComponent;
    }

    protected override void CheckItems(System.ReadOnlySpan<Behavior> range) => ThrowIfAnyNull(range);

    protected override void OnInsert(int index, System.ReadOnlySpan<Behavior> range)
    {
        foreach (var item in range)
        {
            item.AttachTo(_adamantiumComponent);
        }

        base.OnInsert(index, range);
    }

    protected override void OnRemove(int index, System.ReadOnlySpan<Behavior> range)
    {
        foreach (var item in range)
        {
            item.DetachFrom(_adamantiumComponent);
        }

        base.OnRemove(index, range);
    }

    protected override void OnSet(int index, Behavior oldItem, Behavior newItem)
    {
        oldItem?.DetachFrom(_adamantiumComponent);
        newItem?.AttachTo(_adamantiumComponent);
        base.OnSet(index, oldItem, newItem);
    }

    // Detach them while they are still HERE - after the clear they are gone and nothing can be detached from anything.
    protected override void OnClearing(System.ArraySegment<Behavior> items)
    {
        foreach (var behavior in items)
        {
            behavior.DetachFrom(_adamantiumComponent);
        }
        base.OnClearing(items);
    }
}