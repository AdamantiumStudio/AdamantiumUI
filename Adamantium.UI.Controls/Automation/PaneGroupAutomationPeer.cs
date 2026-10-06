using System;
using System.Linq;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;

namespace Adamantium.UI.Controls.Automation;

/// <summary>The peer of a <see cref="PaneGroup"/>: a panel of panes, moved whole - torn out into a window of its own, as
/// a drag by its caption does, or carried with every pane in it to an edge, into the documents or beside another pane.
/// </summary>
public class PaneGroupAutomationPeer : TabControlAutomationPeer, IDockProvider
{
    public PaneGroupAutomationPeer(PaneGroup owner) : base(owner)
    {
    }

    public DockPosition DockPosition =>
        Panes().FirstOrDefault() is { } pane ? DockPaneAutomationPeer.PositionOf(pane.Zone) : DockPosition.None;

    public void SetDockPosition(DockPosition position)
    {
        var group = (PaneGroup)Owner;
        if (position == DockPosition.None)
        {
            var area = AreaHolding(group);
            if (area == null || !area.TearOffGroup(group, group.PointToScreen(default)))
            {
                throw new InvalidOperationException($"'{Name}' may not float, or the application refused it.");
            }

            return;
        }

        foreach (var pane in Panes())
        {
            ((DockPaneAutomationPeer)pane.GetAutomationPeer()).SetDockPosition(position);
        }
    }

    public void DockBeside(AutomationPeer target, DockPosition side)
    {
        var panes = Panes();
        if (panes.Length == 0)
        {
            throw new InvalidOperationException($"'{Name}' holds no panes.");
        }

        ((DockPaneAutomationPeer)panes[0].GetAutomationPeer()).DockBeside(target, side);
        var area = DockPaneAutomationPeer.AreaOf(panes[0]);
        foreach (var pane in panes.Skip(1))
        {
            if (area?.DockInto(pane.Id, panes[0].Id) != true)
            {
                throw new InvalidOperationException($"'{pane.Header}' may not follow '{panes[0].Header}'.");
            }
        }
    }

    private Pane[] Panes() => [.. ((PaneGroup)Owner).Items.OfType<Pane>()];

    private static DockingArea AreaHolding(PaneGroup group)
    {
        for (var parent = group.VisualParent; parent != null; parent = parent.VisualParent)
        {
            if (parent is DockingArea area)
            {
                return area;
            }
        }

        return null;
    }
}
