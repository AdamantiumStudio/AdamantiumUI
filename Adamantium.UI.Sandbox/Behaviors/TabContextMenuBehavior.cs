using System.Linq;
using Adamantium.MVVM;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core.Behaviors;

namespace Adamantium.UI.Sandbox.Behaviors;

/// <summary>The application-built tab context menu, calling the docking area's own API (e.g.
/// <see cref="DockingArea.ClosePane"/>) so its policies apply.</summary>
public class TabContextMenuBehavior : Behavior<DockingArea>
{
    private DockingArea _area;

    protected override void OnAttached(DockingArea area)
    {
        _area = area;
        area.ActivePaneChanged += OnActivePaneChanged;
    }

    protected override void OnDetached(DockingArea area)
    {
        area.ActivePaneChanged -= OnActivePaneChanged;
        _area = null;
    }

    // Panes arrive over time - the markup's at build, the region's whenever something navigates - so the menu is given
    // to whoever hasn't got one yet, each time the active pane changes. A pane opened by code becomes active as it
    // opens, which is exactly when it gets its menu.
    private void OnActivePaneChanged(object sender, System.EventArgs e)
    {
        if (_area == null) return;

        foreach (var pane in _area.Panes.ToList())
        {
            if (pane.Kind != PaneKind.Document || pane.ContextMenu != null) continue;

            pane.ContextMenu = MenuFor(pane);
        }
    }

    private ContextMenu MenuFor(Pane pane)
    {
        var menu = new ContextMenu();

        menu.Items.Add(Item("Close", () => _ = _area.ClosePaneAsync(pane.Id)));
        menu.Items.Add(Item("Close other tabs", () => _ = _area.CloseOtherPanesAsync(pane.Id)));
        menu.Items.Add(Item("Close all tabs in this panel", () => _ = _area.ClosePanesOfGroupAsync(pane.Id)));
        menu.Items.Add(Item("Close all but pinned", () => _ = _area.CloseUnpinnedPanesAsync(pane.Id)));
        menu.Items.Add(Item("Close all tabs (everywhere)", () => _ = _area.CloseAllPanesAsync()));
        menu.Items.Add(Item("Pin / unpin this tab", () => pane.IsPinned = !pane.IsPinned));

        return menu;
    }

    private static MenuItem Item(string header, System.Action execute) =>
        new() { Header = header, Command = new AdamantiumCommand(execute) };
}
