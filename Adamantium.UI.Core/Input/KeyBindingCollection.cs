using Adamantium.Core.Collections;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>The keys set on one element (<see cref="CommandManager.GetInputBindings"/>). Each joins the element's logical
/// tree, and while the element is in a window the window knows of it, so its keys reach it from anywhere in the window.
/// </summary>
public sealed class KeyBindingCollection : TrackingCollection<KeyBinding>
{
    private readonly IAdamantiumComponent owner;
    private IRootVisualComponent root;

    public KeyBindingCollection(IAdamantiumComponent owner)
    {
        this.owner = owner;
        if (owner is IUIComponent element)
        {
            element.AttachedToVisualTreeEvent += OnAttached;
            element.DetachedFromVisualTreeEvent += OnDetached;
            Join(element.RootVisual);
        }
    }

    protected override void OnInsert(int index, KeyBinding item)
    {
        item.AttachTo(owner);
        base.OnInsert(index, item);
    }

    protected override void OnRemoveItem(int index, KeyBinding item)
    {
        item.DetachFrom(owner);
        base.OnRemoveItem(index, item);
    }

    protected override void OnSet(int index, KeyBinding oldItem, KeyBinding newItem)
    {
        oldItem?.DetachFrom(owner);
        newItem?.AttachTo(owner);
        base.OnSet(index, oldItem, newItem);
    }

    protected override void OnClearing(ArraySegment<KeyBinding> items)
    {
        foreach (var binding in items)
        {
            binding.DetachFrom(owner);
        }

        base.OnClearing(items);
    }

    private void OnAttached(object sender, VisualTreeAttachmentEventArgs e) => Join(e.Root);

    private void OnDetached(object sender, VisualTreeAttachmentEventArgs e) => Join(null);

    private void Join(IRootVisualComponent next)
    {
        if (ReferenceEquals(root, next))
        {
            return;
        }

        CommandManager.Unlist(root, (IUIComponent)owner);
        root = next;
        CommandManager.List(root, (IUIComponent)owner);
    }
}
