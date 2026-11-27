using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Printing;

/// <summary>What of an element's own drawing a print cannot put down as vector: a visual brush, a picture whose pixels
/// live on the GPU - a live 3D view, a game - and the effects of its text's glyphs, outlines, shadows, glows and
/// reflections, which the GPU draws from the glyphs' distance fields. It draws nothing; it only looks.</summary>
internal sealed class VectorProbe : IDrawingContext, IDrawingSession
{
    private bool _isVector = true;

    /// <summary>Whether what it draws itself, its subtree aside, is all vector - its text's glyphs too.</summary>
    public bool DrawsVector => _isVector;

    /// <summary>Whether its text wears effects on its glyphs, which a print lays under the glyphs as a picture.</summary>
    public bool DrawsTextEffects { get; private set; }

    /// <summary>Looks at what <paramref name="component"/> draws itself.</summary>
    public static VectorProbe Of(IUIComponent component)
    {
        var probe = new VectorProbe();
        component.RenderReadOnly(probe);
        return probe;
    }

    // A text drawn: its glyphs are vector, their effects are not.
    private IDrawingSession Writes(TextLayout layout, params Brush[] brushes)
    {
        DrawsTextEffects |= layout?.HasGlyphEffects == true;
        return Paints(brushes);
    }

    public IDrawingSession ForControl(IUIComponent component) => this;

    private IDrawingSession Paints(params Brush[] brushes)
    {
        foreach (var brush in brushes)
        {
            if (brush is VisualBrush || brush is TileBrush { ContentSource: { } source } && !PicturePixels.IsPrintable(source, -1))
            {
                _isVector = false;
            }
        }

        return this;
    }

    private IDrawingSession Shows(ImageSource image, int frame, Brush filter)
    {
        if (image != null && !PicturePixels.IsPrintable(image, frame))
        {
            _isVector = false;
        }

        return Paints(filter);
    }

    public IDrawingSession DrawLine(Vector2<double> start, Vector2<double> end, Pen pen) => Paints(pen?.Brush);

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, Pen pen = null) => Paints(brush, pen?.Brush);

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen = null) =>
        Paints(brush, pen?.Brush);

    public IDrawingSession DrawBorder(Brush background, Rect destinationRect, CornerRadius corners, Brush borderBrush,
        Thickness borderThickness) => Paints(background, borderBrush);

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, double startAngle, double sweepAngle,
        EllipseType ellipseType, Pen pen = null, double ringThickness = 0) => Paints(brush, pen?.Brush);

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Pen pen = null,
        double ringThickness = 0, double startAngle = 0) => Paints(brush, pen?.Brush);

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen = null) => Paints(brush, pen?.Brush);

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Matrix4x4F transform) =>
        Paints(brush, pen?.Brush);

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Placement placement) =>
        Paints(brush, pen?.Brush);

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen,
        Placement placement) => Paints(brush, pen?.Brush);

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, Pen pen, Placement placement) =>
        Paints(brush, pen?.Brush);

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, double startAngle, Pen pen,
        Placement placement) => Paints(brush, pen?.Brush);

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners) =>
        Shows(image, -1, filter);

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        Rect sourceUv) => Shows(image, -1, filter);

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4F transform) =>
        Shows(image, -1, filter);

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer) => Shows(image, frameLayer, filter);

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer, Rect sourceUv) => Shows(image, frameLayer, filter);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke) =>
        Writes(textLayout, foreground, background, stroke);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Matrix4x4F transform) =>
        Writes(textLayout, foreground, background, stroke);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Placement placement) =>
        Writes(textLayout, foreground, background, stroke);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects) =>
        Writes(textLayout, foreground, background, stroke);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects,
        RectangleF window) => Writes(textLayout, foreground, background, stroke);

    public IDrawingSession DrawGlyphs(GlyphRun run, Brush fill, Pen stroke, Matrix4x4F transform) =>
        Paints(fill, stroke?.Brush);

    public IDrawingSession DrawColoredMesh(ColoredMesh mesh, Matrix4x4F transform) => this;

    public IDrawingSession PushGroup(DrawingGroupOptions options) => this;

    public IDrawingSession PopGroup() => this;

    public IDrawingSession PushImage(ImageSource image) => Shows(image, -1, null);
}
