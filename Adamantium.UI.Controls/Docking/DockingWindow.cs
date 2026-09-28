namespace Adamantium.UI.Controls.Docking;

/// <summary>The window a floating root lives in, holding an ordinary <see cref="DockingArea"/>. A type of its own so a theme
/// can style it; it has no behavior of its own.</summary>
public class DockingWindow : Window
{
    /// <summary>The area showing this window's root. Held so the docking system can find its way from a window back to
    /// the root it stands for - the window is what the platform hands back from a move, and the root is what the model
    /// knows.</summary>
    public DockingArea Area { get; internal set; }
}
