using Adamantium.Mathematics;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>What a drop target receives, as a <c>DropCommand</c> parameter and as <see cref="DragDropEvents"/> args: read
/// <see cref="Data"/>, set <see cref="Effects"/>. Handled also suppresses the command.</summary>
public class DragDropEventArgs : RoutedEventArgs
{
    public DragDropEventArgs(IDataPackage data, object source, Vector2 position)
    {
        Data = data;
        DragSource = source;
        Position = position;
    }

    /// <summary>The dragged payload.</summary>
    public IDataPackage Data { get; }

    /// <summary>The drag source element (the one that carried <c>DragDrop.DragData</c>). Null for a drag started in
    /// another application. NB: not <c>Source</c> - that one belongs to the routing.</summary>
    public object DragSource { get; }

    /// <summary>The ItemsSource of the source's items host (its ListBox/ItemsControl), if any. Lets a view-model identify the
    /// ORIGIN collection by reference - unambiguous even when equal values live in several lists (after a Copy) - so a Move
    /// removes from the collection actually dragged from, without the view-model touching the visual tree.</summary>
    public object SourceItemsSource { get; set; }

    /// <summary>Pointer position in the drop target's coordinate space.</summary>
    public Vector2 Position { get; }

    /// <summary>What the target will do with the payload - set by the target; drives the cursor and the source's outcome.</summary>
    public DragDropEffects Effects { get; set; } = DragDropEffects.Move;

    /// <summary>Overrides the cursor chosen from <see cref="Effects"/> when set in <c>DragOver</c>; null keeps the
    /// default. Ignored during cross-application drags.</summary>
    public Cursor DragCursor { get; set; }

    /// <summary>Index in the target's collection where the payload should be inserted (the position the insertion line
    /// showed), or -1 to append. Set by the engine from the drop position over an items host. NB: this index is computed
    /// BEFORE the source removes its items, so in a same-list reorder it can be stale - prefer <see cref="InsertBefore"/>.</summary>
    public int InsertIndex { get; set; } = -1;

    /// <summary>The item the payload should be inserted BEFORE (the item that was at the insertion point), or null to
    /// append. Robust to a same-list reorder: this reference keeps its place after the source removes the dragged items,
    /// so <c>target.IndexOf(InsertBefore)</c> gives the right position where a stale numeric index would not.</summary>
    public object InsertBefore { get; set; }

    /// <summary>Hierarchical drops (a TreeView): the item the drop is relative to - a sibling anchor for
    /// <see cref="DropPlacement.Before"/>/<see cref="DropPlacement.After"/>, or the parent for <see cref="DropPlacement.Into"/>.
    /// Null means the root. Ignored for a plain list (see <see cref="InsertBefore"/>).</summary>
    public object DropTarget { get; set; }

    /// <summary>How the drop lands relative to <see cref="DropTarget"/> (before/after sibling, or into as a child).
    /// <see cref="DropPlacement.None"/> for a flat list.</summary>
    public DropPlacement Placement { get; set; } = DropPlacement.None;
}
