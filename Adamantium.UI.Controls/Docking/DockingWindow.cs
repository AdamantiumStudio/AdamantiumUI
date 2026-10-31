using Adamantium.UI.Core;

namespace Adamantium.UI.Controls.Docking;

/// <summary>The window a floating root lives in, holding an ordinary <see cref="DockingArea"/>. A type of its own so a theme
/// can style it. Its place and size are the layout's, so it remembers none of its own: every floating window is this type,
/// and one remembered place would put them all where the last one was closed.</summary>
public class DockingWindow : Window
{
    static DockingWindow()
    {
        RemembersPlacementProperty.OverrideMetadata(typeof(DockingWindow), new PropertyMetadata(false));
    }

    /// <summary>The area showing this window's root. Held so the docking system can find its way from a window back to
    /// the root it stands for - the window is what the platform hands back from a move, and the root is what the model
    /// knows.</summary>
    public DockingArea Area { get; internal set; }
}
