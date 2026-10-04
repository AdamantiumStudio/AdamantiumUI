using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Navigation;

/// <summary>One panel of a docking region as its host reports it: the panes it holds as tabs and where it stands. A
/// description, not the panel itself.</summary>
public sealed class DockedGroup
{
    public DockedGroup(IReadOnlyList<DockedPane> panes, PaneKind kind, DockZone zone, PaneState state, DockedPane front)
    {
        Panes = panes;
        Kind = kind;
        Zone = zone;
        State = state;
        Front = front;
    }

    /// <summary>Its tabs, in order.</summary>
    public IReadOnlyList<DockedPane> Panes { get; }

    /// <summary>The view models of its tabs; a tab without one is not among them.</summary>
    public IReadOnlyList<object> ViewModels => Panes.Where(pane => pane.ViewModel != null).Select(pane => pane.ViewModel).ToList();

    /// <summary>A group of documents - one of the document area's - or of tools.</summary>
    public PaneKind Kind { get; }

    public DockZone Zone { get; }

    /// <summary><see cref="PaneState.Open"/>, or <see cref="PaneState.Collapsed"/> when put away to its strip.</summary>
    public PaneState State { get; }

    /// <summary>The tab at the front, or null when it has none.</summary>
    public DockedPane Front { get; }
}
