namespace Adamantium.UI.Controls.Canvases;

/// <summary>Where a <see cref="CanvasPane"/> sits over the plane. The middle of a canvas is the work, so every
/// placement but two names an EDGE, a corner or a side of the viewport - and the two that do not name something the
/// pane follows instead.</summary>
public enum CanvasPanePlacement
{
    TopLeft,

    /// <summary>Along the top, centered - a toolbar rather than a panel.</summary>
    TopCenter,

    TopRight,

    /// <summary>Against the left edge, centered down it - where a tool rail belongs.</summary>
    Left,

    Right,

    BottomLeft,

    BottomCenter,

    BottomRight,

    /// <summary>Wherever it was put: <see cref="CanvasPane.Anchor"/> says where, as a fraction of the room the pane can
    /// travel across. Dragging a pane sets this - and because it is a property like any other, an application can save
    /// where the user left it and put it back.</summary>
    Free,

    /// <summary>Following the SELECTION rather than the viewport: the pane rides above the selection frame and moves
    /// with it, and it is shown only while something is selected. A context bar is this and nothing else.</summary>
    Selection,

    /// <summary>Along the WHOLE left side, flush with the edge and as tall as the canvas: a side panel that splits the
    /// viewport. The plane is looked at beside it, and every pane placed against an edge or a corner stands beside it
    /// too. It is not carried; its inner edge widens it when the pane can be resized.</summary>
    LeftSide,

    /// <summary>The same as <see cref="LeftSide"/>, along the right side.</summary>
    RightSide
}
