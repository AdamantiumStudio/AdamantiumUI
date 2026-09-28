using System;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>Which grips of the selection frame an item offers; a connection or a curve has no box to resize.</summary>
[Flags]
public enum CanvasHandles
{
    /// <summary>No frame at all. It can still be selected and moved from the list or the keyboard; what it cannot be is
    /// dragged about by a box.</summary>
    None = 0,

    /// <summary>The inside of the frame, which moves what is selected.</summary>
    Body = 1,

    /// <summary>The four corners, which resize in both directions at once.</summary>
    Corners = 2,

    /// <summary>The four middles, which resize in one.</summary>
    Sides = 4,

    /// <summary>Everything - what an ordinary box offers, and the default.</summary>
    All = Body | Corners | Sides
}
