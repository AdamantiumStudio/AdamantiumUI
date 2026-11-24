using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Core.Printing;

/// <summary>A session that draws into another with everything placed by one more matrix - a page scaled and moved into
/// its place on a sheet. Each call goes on as its variant with a matrix or a placement, that matrix put after its own.
/// </summary>
/// <remarks>Text drawn with effects or through a window has no variant with a matrix: it goes on as the plain text.
/// </remarks>
internal sealed class TransformedDrawingSession(IDrawingSession inner, Matrix4x4F transform) : IDrawingSession
{
    private Matrix4x4F Then(Matrix4x4F own) => own * transform;

    private Placement Placed(Placement placement) =>
        new() { Matrix = Then(placement?.Matrix ?? Matrix4x4F.Identity) };

    public IDrawingSession DrawLine(Vector2<double> start, Vector2<double> end, Pen pen)
    {
        var line = new StreamGeometry();
        line.Open().BeginFigure(start, false, false).LineTo(end);
        line.RecalculateBounds();
        inner.DrawGeometry(null, line, pen, transform);
        return this;
    }

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, Pen pen = null) =>
        DrawRectangle(brush, destinationRect, new CornerRadius(0), pen, null);

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen = null) =>
        DrawRectangle(brush, destinationRect, corners, pen, null);

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen,
        Placement placement)
    {
        inner.DrawRectangle(brush, destinationRect, corners, pen, Placed(placement));
        return this;
    }

    public IDrawingSession DrawBorder(Brush background, Rect destinationRect, CornerRadius corners, Brush borderBrush,
        Thickness borderThickness)
    {
        inner.DrawRectangle(background, destinationRect, corners, null, Placed(null));
        if (BorderRing.Of(destinationRect, corners, borderThickness) is { } ring)
        {
            inner.DrawGeometry(borderBrush, ring, null, transform);
        }

        return this;
    }

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, double startAngle, double sweepAngle,
        EllipseType ellipseType, Pen pen = null, double ringThickness = 0)
    {
        if (ringThickness > 0 && ringThickness * 2 < Math.Min(destinationRect.Width, destinationRect.Height))
        {
            var half = ringThickness / 2;
            var middle = new Rect(destinationRect.X + half, destinationRect.Y + half,
                destinationRect.Width - ringThickness, destinationRect.Height - ringThickness);
            var whole = new ArcSpan(startAngle, sweepAngle).IsWhole;
            inner.DrawGeometry(null,
                new EllipseGeometry(middle, startAngle, sweepAngle, whole ? ellipseType : EllipseType.EdgeToEdge),
                new Pen(brush, ringThickness), transform);
            return this;
        }

        inner.DrawGeometry(brush, new EllipseGeometry(destinationRect, startAngle, sweepAngle, ellipseType), pen,
            transform);
        return this;
    }

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, Pen pen, Placement placement)
    {
        inner.DrawEllipse(destinationRect, brush, pen, Placed(placement));
        return this;
    }

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Pen pen = null,
        double ringThickness = 0, double startAngle = 0)
    {
        inner.DrawGeometry(brush, RegularPolygonGeometry.Build(destinationRect, corners, startAngle, ringThickness), pen,
            transform);
        return this;
    }

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, double startAngle, Pen pen,
        Placement placement)
    {
        inner.DrawRegularPolygon(brush, destinationRect, corners, startAngle, pen, Placed(placement));
        return this;
    }

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen = null)
    {
        inner.DrawGeometry(brush, geometry, pen, transform);
        return this;
    }

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Matrix4x4F matrix)
    {
        inner.DrawGeometry(brush, geometry, pen, Then(matrix));
        return this;
    }

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Placement placement)
    {
        inner.DrawGeometry(brush, geometry, pen, Placed(placement));
        return this;
    }

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners) =>
        Picture(image, filter, destinationRect, corners, new Rect(0, 0, 1, 1));

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        Rect sourceUv) => Picture(image, filter, destinationRect, corners, sourceUv);

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4F matrix)
    {
        inner.DrawImage(image, filter, destinationRect, Then(matrix));
        return this;
    }

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer) =>
        DrawImageFrame(image, filter, destinationRect, corners, frameLayer, new Rect(0, 0, 1, 1));

    /// <summary>The frame as a picture of its own: the variant with a matrix draws a whole picture.</summary>
    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer, Rect sourceUv)
    {
        var frame = image is BitmapImage animated && frameLayer >= 0 && frameLayer < animated.FrameCount
            ? animated.GetFrame((uint)frameLayer)
            : image;
        return Picture(frame, filter, destinationRect, corners, sourceUv);
    }

    /// <summary>The part <paramref name="uv"/> of the picture in <paramref name="rect"/>, cut to it and its corners:
    /// the variant with a matrix draws a whole picture into a rect, uncut.</summary>
    private IDrawingSession Picture(ImageSource image, Brush filter, Rect rect, CornerRadius corners, Rect uv)
    {
        if (!(uv.Width > 0) || !(uv.Height > 0))
        {
            return this;
        }

        var whole = uv == new Rect(0, 0, 1, 1);
        var square = corners.TopLeft <= 0 && corners.TopRight <= 0 && corners.BottomRight <= 0 &&
                     corners.BottomLeft <= 0;
        var cut = !whole || !square;
        if (cut)
        {
            inner.PushGroup(new DrawingGroupOptions
            {
                Clip = new RectangleGeometry(rect, corners),
                ClipTransform = transform,
            });
        }

        var width = rect.Width / uv.Width;
        var height = rect.Height / uv.Height;
        inner.DrawImage(image, filter, new Rect(rect.X - uv.X * width, rect.Y - uv.Y * height, width, height),
            transform);
        if (cut)
        {
            inner.PopGroup();
        }

        return this;
    }

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke)
    {
        inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke, transform);
        return this;
    }

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Matrix4x4F matrix)
    {
        inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke, Then(matrix));
        return this;
    }

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Placement placement)
    {
        inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke,
            Placed(placement));
        return this;
    }

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects) =>
        DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects,
        RectangleF window) =>
        DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke);

    public IDrawingSession DrawGlyphs(GlyphRun run, Brush fill, Pen stroke, Matrix4x4F matrix)
    {
        inner.DrawGlyphs(run, fill, stroke, Then(matrix));
        return this;
    }

    public IDrawingSession DrawColoredMesh(ColoredMesh mesh, Matrix4x4F matrix)
    {
        inner.DrawColoredMesh(mesh, Then(matrix));
        return this;
    }

    public IDrawingSession PushGroup(DrawingGroupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<PlacedGlyphRun> glyphs = null;
        if (options.TextClip is { Count: > 0 } clip)
        {
            glyphs = new List<PlacedGlyphRun>(clip.Count);
            foreach (var placed in clip)
            {
                glyphs.Add(placed with { Transform = Then(placed.Transform) });
            }
        }

        inner.PushGroup(new DrawingGroupOptions
        {
            Clip = options.Clip,
            ClipTransform = options.ClipPlacement != null ? Matrix4x4F.Identity : Then(options.ClipTransform),
            ClipPlacement = options.ClipPlacement != null ? Placed(options.ClipPlacement) : null,
            TextClip = glyphs,
            Mask = options.Mask,
            MaskTransform = Then(options.MaskTransform),
            MaskKind = options.MaskKind,
            MaskBackdrop = options.MaskBackdrop,
            BlendMode = options.BlendMode,
            Opacity = options.Opacity,
        });
        return this;
    }

    public IDrawingSession PopGroup()
    {
        inner.PopGroup();
        return this;
    }

    public IDrawingSession PushImage(ImageSource image)
    {
        inner.PushImage(image);
        return this;
    }
}
