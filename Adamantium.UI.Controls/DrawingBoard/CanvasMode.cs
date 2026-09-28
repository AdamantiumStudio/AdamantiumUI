namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>What the canvas is used as: a drawing or a graph, which share only plane, camera and selection. The mode picks
/// which one's tools and items are live.</summary>
public enum CanvasMode
{
    /// <summary>Ink, shapes, text, images - a drawing.</summary>
    Drawing,

    /// <summary>Nodes and the wires between them - a graph.</summary>
    Nodes
}
