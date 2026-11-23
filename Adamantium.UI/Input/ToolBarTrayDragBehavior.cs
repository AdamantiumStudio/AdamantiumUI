using System.Collections.Specialized;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Input;

/// <summary>
/// Lets the user carry the bars of a <see cref="ToolBarTray"/> by their grips with <see cref="DragDrop"/> - its ghost, its
/// drop over any tray of the window that takes the bar - and drop bars from other trays into this one. The tray itself
/// moves nothing: it says where a bar would land (<see cref="ToolBarTray.ShowDropPlace"/>) and puts it there
/// (<see cref="ToolBarTray.DropBar"/>). Attach:
/// <code>&lt;ToolBarTray&gt;&lt;ToolBarTray.Behaviors&gt;&lt;ToolBarTrayDragBehavior/&gt;&lt;/ToolBarTray.Behaviors&gt;&lt;/ToolBarTray&gt;</code>
/// </summary>
public class ToolBarTrayDragBehavior : Behavior<ToolBarTray>
{
    private readonly DragDropEventHandler _over;
    private readonly DragDropEventHandler _leave;
    private readonly DragDropEventHandler _drop;
    private readonly DragDropEventHandler _completed;
    private readonly PointerEventHandler _hover;

    public ToolBarTrayDragBehavior()
    {
        _over = OnDragOver;
        _leave = OnDragLeave;
        _drop = OnDrop;
        _completed = OnDragCompleted;
        _hover = OnPointerMoved;
    }

    protected override void OnAttached(ToolBarTray tray)
    {
        DragDrop.SetAllowDrop(tray, true);
        tray.AddHandler(DragDropEvents.DragOverEvent, _over);
        tray.AddHandler(DragDropEvents.DragLeaveEvent, _leave);
        tray.AddHandler(DragDropEvents.DropEvent, _drop);
        // Bars the tray makes as it lays out, from its items' source, are made carriable once the pointer reaches them.
        tray.AddHandler(PointerEvents.PreviewPointerMovedEvent, _hover, handledEventsToo: true);
        tray.Items.CollectionChanged += OnItemsChanged;
        MarkBars(tray);
    }

    protected override void OnDetached(ToolBarTray tray)
    {
        tray.HideDropPlace();
        DragDrop.SetAllowDrop(tray, false);
        tray.RemoveHandler(DragDropEvents.DragOverEvent, _over);
        tray.RemoveHandler(DragDropEvents.DragLeaveEvent, _leave);
        tray.RemoveHandler(DragDropEvents.DropEvent, _drop);
        tray.RemoveHandler(PointerEvents.PreviewPointerMovedEvent, _hover);
        tray.Items.CollectionChanged -= OnItemsChanged;
        foreach (var bar in tray.Bars)
        {
            Unmark(bar);
        }
    }

    private void OnItemsChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (AssociatedComponent is { } tray)
        {
            MarkBars(tray);
        }
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (AssociatedComponent is { } tray)
        {
            MarkBars(tray);
        }
    }

    private void MarkBars(ToolBarTray tray)
    {
        foreach (var bar in tray.Bars)
        {
            Mark(bar);
        }
    }

    // A bar is carried by its grip - the template's DragHandle - and shows as itself under the pointer, held where it
    // was pressed.
    private void Mark(ToolBar bar)
    {
        if (DragDrop.GetAllowDrag(bar))
        {
            return;
        }

        DragDrop.SetAllowDrag(bar, true);
        DragDrop.SetDragData(bar, bar);
        DragDrop.SetGhostAnchor(bar, DragGhostAnchor.Grab);
        bar.AddHandler(DragDropEvents.DragCompletedEvent, _completed);
    }

    private void Unmark(ToolBar bar)
    {
        DragDrop.SetAllowDrag(bar, false);
        DragDrop.SetDragData(bar, null);
        bar.RemoveHandler(DragDropEvents.DragCompletedEvent, _completed);
    }

    private void OnDragOver(object sender, DragDropEventArgs e)
    {
        if (AssociatedComponent is not { } tray || e.Data?.Get<ToolBar>() is not { } bar)
        {
            return;
        }

        ToolBarTray.SetIsMoving(bar, true);
        e.Effects = tray.ShowDropPlace(bar, e.Position) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragDropEventArgs e) => AssociatedComponent?.HideDropPlace();

    private void OnDrop(object sender, DragDropEventArgs e)
    {
        if (AssociatedComponent is not { } tray || e.Data?.Get<ToolBar>() is not { } bar)
        {
            return;
        }

        ToolBarTray.SetIsMoving(bar, false);
        tray.DropBar(bar, e.Position);
        e.Handled = true;
    }

    // The tray it lands in has already put it there; the bar only stops being carried. Handled, so nothing removes it.
    private void OnDragCompleted(object sender, DragDropEventArgs e)
    {
        if (sender is ToolBar bar)
        {
            ToolBarTray.SetIsMoving(bar, false);
        }

        e.Handled = true;
    }
}
