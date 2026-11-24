namespace Adamantium.UI.Core;

/// <summary>How an element and its subtree reach the screen each frame.</summary>
public enum DrawingMode
{
    /// <summary>From their shapes, every frame: exact at any zoom.</summary>
    Vector,

    /// <summary>From tiles of a texture in the element's own space, drawn once: the camera moves and scales them, and
    /// they are drawn again only when the zoom has gone a step further or what they show changes. For heavy content
    /// that holds still - a page of a map, a large diagram.</summary>
    Raster,
}
