using Adamantium.UI.Core;

namespace Adamantium.UI.Controls;

/// <summary>One of a tab strip's two lists (pinned or ordinary, see <see cref="TabControl.PinnedItems"/>). Defers every
/// container decision to the owning <see cref="TabControl"/>, so items become real <see cref="TabItem"/>s.</summary>
public class TabItemsControl : ItemsControl
{
    /// <summary>The strip this list belongs to. It is a template part of that control, so the templated parent IS the
    /// owner - no walking the tree and no guessing.</summary>
    private TabControl Owner => TemplatedParent as TabControl;

    protected internal override bool IsItemItsOwnContainer(object item) =>
        Owner?.IsItemItsOwnContainer(item) ?? base.IsItemItsOwnContainer(item);

    protected internal override IUIComponent GetContainerForItem(object item) =>
        Owner?.GetContainerForItem(item) ?? base.GetContainerForItem(item);

    protected internal override void PrepareContainer(IUIComponent container, object item)
    {
        if (Owner is { } owner) owner.PrepareContainer(container, item);
        else base.PrepareContainer(container, item);
    }

    protected internal override void ClearContainer(IUIComponent container)
    {
        if (Owner is { } owner) owner.ClearContainer(container);
        else base.ClearContainer(container);
    }
}
