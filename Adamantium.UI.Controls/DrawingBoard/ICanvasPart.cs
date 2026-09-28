namespace Adamantium.UI.Controls.DrawingBoard;

/// <summary>A piece of an <see cref="InfiniteCanvas"/>'s chrome (rail, view bar, map, inspector). Its
/// <see cref="CanvasPane"/> hands it the canvas directly, since a hidden pane has no tree for a binding.</summary>
public interface ICanvasPart
{
    /// <summary>The plane this piece is for.</summary>
    InfiniteCanvas Canvas { get; set; }
}
