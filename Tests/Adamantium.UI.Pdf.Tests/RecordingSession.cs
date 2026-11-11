using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>What a page drew, call by call in order: what the tests check, with no GPU.</summary>
internal sealed class RecordingSession : IDrawingSession
{
    public List<object> Calls { get; } = [];

    public int Depth { get; private set; }

    public int DeepestGroup { get; private set; }

    /// <summary>Takes away the group a page opens first, cutting everything to its crop box, and returns its clip: what
    /// is left is what the page's content drew, depths counted inside that group.</summary>
    public Geometry UnwrapPage()
    {
        if (Calls.Count < 2 || Calls[0] is not OpenedGroup { Depth: 0 } page || Calls[^1] is not ClosedGroup)
        {
            throw new System.InvalidOperationException("A page draws inside the group of its crop box.");
        }

        Calls.RemoveAt(Calls.Count - 1);
        Calls.RemoveAt(0);
        for (var i = 0; i < Calls.Count; i++)
        {
            Calls[i] = Calls[i] switch
            {
                DrawnGeometry g => g with { Depth = g.Depth - 1 },
                DrawnGlyphs g => g with { Depth = g.Depth - 1 },
                DrawnImage g => g with { Depth = g.Depth - 1 },
                DrawnMesh g => g with { Depth = g.Depth - 1 },
                OpenedGroup g => g with { Depth = g.Depth - 1 },
                var other => other
            };
        }

        DeepestGroup--;
        return page.Options.Clip;
    }

    public IEnumerable<T> All<T>()
    {
        foreach (var call in Calls)
        {
            if (call is T found)
            {
                yield return found;
            }
        }
    }

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Matrix4x4<float> transform)
    {
        Calls.Add(new DrawnGeometry(brush, geometry, pen, transform, Depth));
        return this;
    }

    public IDrawingSession DrawGlyphs(GlyphRun run, Brush fill, Pen stroke, Matrix4x4<float> transform)
    {
        Calls.Add(new DrawnGlyphs(run, fill, stroke, transform, Depth));
        return this;
    }

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4<float> transform)
    {
        Calls.Add(new DrawnImage(image, transform, Depth));
        return this;
    }

    public IDrawingSession DrawColoredMesh(ColoredMesh mesh, Matrix4x4<float> transform)
    {
        Calls.Add(new DrawnMesh(mesh, transform, Depth));
        return this;
    }

    public IDrawingSession PushGroup(DrawingGroupOptions options)
    {
        Calls.Add(new OpenedGroup(options, Depth));
        Depth++;
        DeepestGroup = System.Math.Max(DeepestGroup, Depth);
        return this;
    }

    public IDrawingSession PopGroup()
    {
        Depth--;
        Calls.Add(ClosedGroup.Instance);
        return this;
    }

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen = null) =>
        DrawGeometry(brush, geometry, pen, Matrix4x4<float>.Identity);

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Placement placement) =>
        DrawGeometry(brush, geometry, pen, placement.Matrix);

    public IDrawingSession DrawLine(Vector2<double> start, Vector2<double> end, Pen pen) => Unexpected();

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, Pen pen = null) => Unexpected();

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen = null) =>
        Unexpected();

    public IDrawingSession DrawBorder(Brush background, Rect destinationRect, CornerRadius corners, Brush borderBrush,
        Thickness borderThickness) => Unexpected();

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, double startAngle, double sweepAngle,
        EllipseType ellipseType, Pen pen = null, double ringThickness = 0) => Unexpected();

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Pen pen = null,
        double ringThickness = 0, double startAngle = 0) => Unexpected();

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen,
        Placement placement) => Unexpected();

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, Pen pen, Placement placement) => Unexpected();

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, double startAngle, Pen pen,
        Placement placement) => Unexpected();

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners) =>
        Unexpected();

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        Rect sourceUv) => Unexpected();

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer) => Unexpected();

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer, Rect sourceUv) => Unexpected();

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke) => Unexpected();

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Matrix4x4<float> transform) =>
        Unexpected();

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Placement placement) => Unexpected();

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects) =>
        Unexpected();

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects,
        Rectangle<float> window) => Unexpected();

    public IDrawingSession PushImage(ImageSource image) => Unexpected();

    // A page draws through paths, glyphs, placed images and groups only.
    private static IDrawingSession Unexpected() =>
        throw new System.InvalidOperationException("A PDF page drew through a call it has no use for.");
}
