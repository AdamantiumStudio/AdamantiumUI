using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Adamantium.Navigation;

/// <summary>The docking control's side of a <see cref="DockingRegion"/>: what it holds and what only it can do.
/// Implemented by the control's region adapter; view models use <see cref="IDockingRegion"/>.</summary>
public interface IDockingHost
{
    /// <summary>Every pane, closed tools included, in layout order.</summary>
    IReadOnlyList<DockedPane> Panes { get; }

    /// <summary>Every panel, documents' and tools', in layout order - the ones put away along the edges included.</summary>
    IReadOnlyList<DockedGroup> Groups { get; }

    /// <summary>Brings a pane to the front - back into the layout if it is a closed tool.</summary>
    bool Activate(string paneId);

    /// <summary>Closes panes one by one the way their close button does, asking the application first; returns how many
    /// closed.</summary>
    Task<int> CloseAsync(IReadOnlyList<string> paneIds);

    /// <summary>Docks a pane against one side of the panel holding another.</summary>
    bool DockBeside(string paneId, string targetPaneId, DockZone side);

    /// <summary>Raised after anything in <see cref="Panes"/> may have changed.</summary>
    event EventHandler Changed;
}
