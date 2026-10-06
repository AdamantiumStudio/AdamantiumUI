using System;
using Adamantium.Navigation;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a docked <see cref="Pane"/>: a tab, moved to an edge, into the documents or out into a window of
/// its own as its <see cref="Pane.Zone"/> moves it, and docked beside another pane as a drop on that panel's side does.
/// </summary>
public class DockPaneAutomationPeer : TabItemAutomationPeer, IDockProvider
{
    public DockPaneAutomationPeer(Pane owner) : base(owner)
    {
    }

    public DockPosition DockPosition => PositionOf(((Pane)Owner).Zone);

    public void SetDockPosition(DockPosition position)
    {
        var pane = (Pane)Owner;
        var zone = ZoneOf(position);
        pane.SetCurrentValue(Pane.ZoneProperty, zone);
        if (pane.Zone != zone)
        {
            throw new InvalidOperationException($"'{Name}' may not go to {position}: it is not allowed there, or the " +
                                                "application refused the move.");
        }
    }

    public void DockBeside(AutomationPeer target, DockPosition side)
    {
        var pane = (Pane)Owner;
        if (target is not DockPaneAutomationPeer { Owner: Pane beside } || AreaOf(pane) is not { } area)
        {
            throw new InvalidOperationException("Both must be panes of one docking layout.");
        }

        var docked = side switch
        {
            DockPosition.Fill => area.DockInto(pane.Id, beside.Id),
            DockPosition.None => false,
            _ => area.DockBeside(pane.Id, beside.Id, ZoneOf(side))
        };
        if (!docked)
        {
            throw new InvalidOperationException($"'{Name}' may not dock {side} of '{target.Name}'.");
        }
    }

    /// <summary>The panel showing the pane, also when the pane is alone in a floating window and shown without its tab.
    /// </summary>
    public override AutomationPeer GetParent() =>
        base.GetParent() ?? Showing().Group?.GetAutomationPeer();

    internal static DockingArea AreaOf(Pane pane) => pane.Keeper;

    private (DockingArea Area, PaneGroup Group) Showing() =>
        ((Pane)Owner).Keeper?.Showing(((Pane)Owner).Id) ?? default;

    internal static DockPosition PositionOf(DockZone zone) => zone switch
    {
        DockZone.Top => DockPosition.Top,
        DockZone.Left => DockPosition.Left,
        DockZone.Bottom => DockPosition.Bottom,
        DockZone.Right => DockPosition.Right,
        DockZone.Center => DockPosition.Fill,
        _ => DockPosition.None
    };

    internal static DockZone ZoneOf(DockPosition position) => position switch
    {
        DockPosition.Top => DockZone.Top,
        DockPosition.Left => DockZone.Left,
        DockPosition.Bottom => DockZone.Bottom,
        DockPosition.Right => DockZone.Right,
        DockPosition.Fill => DockZone.Center,
        _ => DockZone.Floating
    };
}
