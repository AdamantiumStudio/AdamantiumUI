namespace Adamantium.Navigation;

/// <summary>Whether a docked pane is in the layout, folded away, or closed and kept.</summary>
public enum PaneState
{
    /// <summary>In the layout - docked, or in a window of its own.</summary>
    Open,

    /// <summary>Its panel is put away to the strip along an edge.</summary>
    Collapsed,

    /// <summary>A tool that was closed and is kept, to be brought back.</summary>
    Hidden
}
