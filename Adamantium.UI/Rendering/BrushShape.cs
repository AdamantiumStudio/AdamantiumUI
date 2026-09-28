using Adamantium.Mathematics;
using Adamantium.UI.Rendering.Payloads;

namespace Adamantium.UI.Rendering;

// The shape (rounded rect, ellipse, polygon) a brush batch fills, shared with the shader's BrushShapeDistance. It rides
// in the corner-radius fields: a negative sentinel for non-rects, and the polygon's numbers in the radii.
internal readonly record struct BrushShape(BrushShapeKind Kind, Vector4F Numbers)
{
    public static readonly BrushShape Rect = new(BrushShapeKind.RoundedRect, Vector4F.Zero);

    public static readonly BrushShape Ellipse = new(BrushShapeKind.Ellipse, Vector4F.Zero);

    public static BrushShape Polygon(RegularPolygonPayload payload, float scale) =>
        new(BrushShapeKind.Polygon, RegularPolygonCollector.ShapeNumbers(payload, scale));

    /// <summary>What goes into the record's corner-radius slot: a rect passes the largest of its own radii, the other
    /// two a sentinel the pixel shader reads as "not a rect".</summary>
    public float RadiusFlag(Vector4F radii) => Kind switch
    {
        BrushShapeKind.Ellipse => -1f,
        BrushShapeKind.Polygon => -2f,
        _ => RectBatchCollector.MaxOf(radii)
    };

    /// <summary>What goes into the record's four corner radii: a polygon's own numbers, a rect's radii, nothing at all
    /// for an ellipse.</summary>
    public Vector4F RadiiFor(Vector4F rectRadii) => Kind switch
    {
        BrushShapeKind.Ellipse => Vector4F.Zero,
        BrushShapeKind.Polygon => Numbers,
        _ => rectRadii
    };
}
