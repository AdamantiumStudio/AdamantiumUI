using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.Input;

namespace Adamantium.UI.Input;

/// <summary>
/// Lets the user carry tool bars by their grips with <see cref="DragDrop"/> - its ghost, its drop over a tray - in its host:
/// a <see cref="ToolBarTray"/>, or a control with trays of its own, as a text editor's tool bar, whose template it need not
/// reach into. Every bar in the host is carried; a bar let go of over a tray of the host lands where the tray says
/// (<see cref="ToolBarTray.ShowDropPlace"/>, <see cref="ToolBarTray.DropBar"/>) - the trays themselves move nothing.
/// Attach:
/// <code>&lt;ToolBarTray&gt;&lt;ToolBarTray.Behaviors&gt;&lt;ToolBarDragBehavior/&gt;&lt;/ToolBarTray.Behaviors&gt;&lt;/ToolBarTray&gt;</code>
/// </summary>
public class ToolBarDragBehavior : Behavior<InputUIComponent>
{
    private readonly DragDropEventHandler _over;
    private readonly DragDropEventHandler _leave;
    private readonly DragDropEventHandler _drop;
    private readonly DragDropEventHandler _completed;
    private readonly PointerEventHandler _hover;
    private readonly List<ToolBarTray> _trays = [];
    private ToolBarTray _shown;

    public ToolBarDragBehavior()
    {
        _over = OnDragOver;
        _leave = OnDragLeave;
        _drop = OnDrop;
        _completed = OnDragCompleted;
        _hover = OnPointerMoved;
    }

    protected override void OnAttached(InputUIComponent host)
    {
        DragDrop.SetAllowDrop(host, true);
        host.AddHandler(DragDropEvents.DragOverEvent, _over);
        host.AddHandler(DragDropEvents.DragLeaveEvent, _leave);
        host.AddHandler(DragDropEvents.DropEvent, _drop);
        // The bars a host shows - its template's, its items' - are made carriable once the pointer reaches them.
        host.AddHandler(PointerEvents.PreviewPointerMovedEvent, _hover, handledEventsToo: true);
    }

    protected override void OnDetached(InputUIComponent host)
    {
        HidePlace();
        DragDrop.SetAllowDrop(host, false);
        host.RemoveHandler(DragDropEvents.DragOverEvent, _over);
        host.RemoveHandler(DragDropEvents.DragLeaveEvent, _leave);
        host.RemoveHandler(DragDropEvents.DropEvent, _drop);
        host.RemoveHandler(PointerEvents.PreviewPointerMovedEvent, _hover);
        foreach (var bar in host.GetVisualDescendants().OfType<ToolBar>())
        {
            Unmark(bar);
        }
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (AssociatedComponent is not { } host)
        {
            return;
        }

        if (host is ToolBar own)
        {
            Mark(own);
        }

        foreach (var bar in host.GetVisualDescendants().OfType<ToolBar>())
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

    // The host's tray under the point - the host's coordinates - that takes the bar there, if any.
    private ToolBarTray TrayAt(UIComponent host, ToolBar bar, Vector2<double> point, out Vector2<double> inTray)
    {
        _trays.Clear();
        if (host is ToolBarTray self)
        {
            _trays.Add(self);
        }

        _trays.AddRange(host.GetVisualDescendants().OfType<ToolBarTray>());
        foreach (var tray in _trays)
        {
            var local = host.TranslatePoint(point, tray);
            if (tray.ShowDropPlace(bar, local))
            {
                inTray = local;
                return tray;
            }
        }

        inTray = default;
        return null;
    }

    private void OnDragOver(object sender, DragDropEventArgs e)
    {
        if (AssociatedComponent is not { } host || e.Data?.Get<ToolBar>() is not { } bar)
        {
            return;
        }

        ToolBarTray.SetIsMoving(bar, true);
        var tray = TrayAt(host, bar, e.Position, out _);
        if (!ReferenceEquals(tray, _shown))
        {
            _shown?.HideDropPlace();
            _shown = tray;
        }

        e.Effects = tray != null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragDropEventArgs e) => HidePlace();

    private void HidePlace()
    {
        _shown?.HideDropPlace();
        _shown = null;
    }

    private void OnDrop(object sender, DragDropEventArgs e)
    {
        if (AssociatedComponent is not { } host || e.Data?.Get<ToolBar>() is not { } bar)
        {
            return;
        }

        HidePlace();
        ToolBarTray.SetIsMoving(bar, false);
        TrayAt(host, bar, e.Position, out var inTray)?.DropBar(bar, inTray);
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
