namespace Adamantium.UI.Controls.Canvases;

/// <summary>What the mouse wheel does to an <see cref="InfiniteCanvas"/>'s camera.</summary>
public enum CanvasWheelMode
{
    /// <summary>Zooms about the pointer - a drawing's plane, looked at from anywhere.</summary>
    Zoom,

    /// <summary>Scrolls, as through a document: down and up, across with Shift or a tilted wheel; Ctrl with the wheel,
    /// and a pinch, zoom about the pointer.</summary>
    Scroll,

    /// <summary>Leaves the wheel alone, to whatever holds the canvas.</summary>
    None,
}
