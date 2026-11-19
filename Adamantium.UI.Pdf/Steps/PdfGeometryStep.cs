using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>A path filled, stroked or both.</summary>
internal sealed class PdfGeometryStep(Geometry geometry, Brush fill, Pen stroke, Matrix4x4F matrix) : PdfDrawStep
{
    public Geometry Geometry { get; } = geometry;

    public Brush Fill { get; } = fill;

    public Pen Stroke { get; } = stroke;

    /// <summary>Where the path stands in the page.</summary>
    public Matrix4x4F Matrix { get; } = matrix;

    /// <summary>Whether it is a path the page's content draws, not a cover laid by a pattern or a mask - what
    /// <see cref="PdfPageDrawing.CollectPaths"/> gives out.</summary>
    public bool IsPagePath { get; init; }

    public override void Draw(IDrawingSession session, Matrix4x4F transform) =>
        session.DrawGeometry(Fill, Geometry, Stroke, Matrix * transform);
}
