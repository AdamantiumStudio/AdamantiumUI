using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Core.Input;

/// <summary>Routed drag-drop events for controls, raised on the nearest <c>AllowDrop</c> element: Preview tunnels, then the
/// plain event bubbles with shared args. <see cref="DragOverEvent"/> decides <c>Effects</c>.</summary>
public static class DragDropEvents
{
    /// <summary>A drag entered this drop target.</summary>
    public static readonly RoutedEvent DragEnterEvent = EventManager.RegisterRoutedEvent("DragEnter",
        RoutingStrategy.Bubble, typeof(DragDropEventHandler), typeof(DragDropEvents));

    /// <summary>A drag is moving over this drop target - set <c>Effects</c> here to say what would happen (and
    /// <c>DragDropEffects.None</c> to refuse the payload).</summary>
    public static readonly RoutedEvent DragOverEvent = EventManager.RegisterRoutedEvent("DragOver",
        RoutingStrategy.Bubble, typeof(DragDropEventHandler), typeof(DragDropEvents));

    /// <summary>The drag left this drop target (moved to another one, off the window, or ended).</summary>
    public static readonly RoutedEvent DragLeaveEvent = EventManager.RegisterRoutedEvent("DragLeave",
        RoutingStrategy.Bubble, typeof(DragDropEventHandler), typeof(DragDropEvents));

    /// <summary>The payload was dropped on this target. Fires only when the drop is going ahead (<c>Effects</c> is not
    /// None), after the source has removed on a Move - see the ordering note in <c>DragDrop.CompleteDrop</c>.</summary>
    public static readonly RoutedEvent DropEvent = EventManager.RegisterRoutedEvent("Drop",
        RoutingStrategy.Bubble, typeof(DragDropEventHandler), typeof(DragDropEvents));

    // The tunnelling half: a CONTAINER sees the drag before the element it contains does, which is the only way to veto
    // from above (a locked panel refusing every drop inside it, a parent that answers for its children).
    /// <summary>Tunnels ahead of <see cref="DragEnterEvent"/>.</summary>
    public static readonly RoutedEvent PreviewDragEnterEvent = EventManager.RegisterRoutedEvent("PreviewDragEnter",
        RoutingStrategy.Tunnel, typeof(DragDropEventHandler), typeof(DragDropEvents));

    /// <summary>Tunnels ahead of <see cref="DragOverEvent"/> - set <c>Handled</c> here to answer for the whole subtree.</summary>
    public static readonly RoutedEvent PreviewDragOverEvent = EventManager.RegisterRoutedEvent("PreviewDragOver",
        RoutingStrategy.Tunnel, typeof(DragDropEventHandler), typeof(DragDropEvents));

    /// <summary>Tunnels ahead of <see cref="DragLeaveEvent"/>.</summary>
    public static readonly RoutedEvent PreviewDragLeaveEvent = EventManager.RegisterRoutedEvent("PreviewDragLeave",
        RoutingStrategy.Tunnel, typeof(DragDropEventHandler), typeof(DragDropEvents));

    /// <summary>Tunnels ahead of <see cref="DropEvent"/>.</summary>
    public static readonly RoutedEvent PreviewDropEvent = EventManager.RegisterRoutedEvent("PreviewDrop",
        RoutingStrategy.Tunnel, typeof(DragDropEventHandler), typeof(DragDropEvents));
}
