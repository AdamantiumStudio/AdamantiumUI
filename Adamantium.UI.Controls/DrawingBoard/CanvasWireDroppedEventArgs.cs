using System;
using Adamantium.Mathematics;

namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A wire dropped on empty plane, offered to the application (typically to open a node list and wire the pick);
/// unanswered, the wire vanishes.</summary>
public sealed class CanvasWireDroppedEventArgs : EventArgs
{
    /// <summary>The node the wire came out of.</summary>
    public ElementItem FromItem { get; init; }

    /// <summary>The socket it came out of - which says what the new node has to offer to be worth making, and which
    /// end of it the wire should arrive at.</summary>
    public CanvasNodePin FromPin { get; init; }

    /// <summary>Where it was let go, in WORLD units - where the new node goes, and where a list of them should open.
    /// </summary>
    public Vector2 World { get; init; }

    /// <summary>Where it was let go on the SCREEN, in pixels - what a popup is placed by.</summary>
    public Vector2 Screen { get; init; }

    /// <summary>Set by whoever takes the offer. Nobody taking it is the ordinary case and not a failure: a wire
    /// dropped on nothing is a gesture abandoned.</summary>
    public bool Handled { get; set; }
}
