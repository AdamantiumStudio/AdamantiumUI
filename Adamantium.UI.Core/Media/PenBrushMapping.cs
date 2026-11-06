namespace Adamantium.UI.Core.Media;

/// <summary>How a stroke's brush is laid onto it.</summary>
public enum PenBrushMapping
{
    /// <summary>Over the shape's bounds, as a fill brush is: the stroke shows the part of the brush it passes over.</summary>
    Bounds,

    /// <summary>Along the line: the brush's box is the stroke unrolled - its length by its thickness, from where the line
    /// starts - so a gradient runs from end to end, a pattern or a picture follows every bend.</summary>
    Along,
}
