namespace Adamantium.UI.Core.Media;

/// <summary>What a <see cref="Pen.Thickness"/> is measured in.</summary>
public enum PenThicknessUnit
{
    /// <summary>The units of the space the stroke is drawn in: a zoom makes the line wider.</summary>
    Local,

    /// <summary>The window's units, as layout counts them - the same on any monitor, a monitor's own pixels being the
    /// business of its scale: the line stays as wide in the window under any zoom - a road on a map.</summary>
    Window
}
