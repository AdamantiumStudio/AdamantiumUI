using System.Collections.Generic;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Printing;

/// <summary>An element's drawing in two goes around its text, so pictures can go between them - its inner shadows and
/// glows, its glyphs' effects, which the screen lays over its fills and under its glyphs. The first go passes on what it
/// draws before its first text, and that text's background; the second, that text - its background left out - and all it
/// draws after, an underline or a strikethrough over the letters as on the screen, a further text with its background.
/// Groups go on in both goes, so each is whole.</summary>
/// <remarks>Made for an element that draws one text, as text blocks do. One that draws more and wears an inner band or
/// glyph effects has what it draws after its first text laid over the pictures.</remarks>
internal sealed class AroundTextSession(IDrawingSession inner, bool fromText) : IDrawingSession
{
    private bool _textSeen;

    // A shape: in the first go before any text, in the second after it.
    private IDrawingSession Shape(System.Func<IDrawingSession> draw)
    {
        if (fromText == _textSeen)
        {
            draw();
        }

        return this;
    }

    // A text. The first: its background, as the screen lays it, in the first go; itself without it in the second. A
    // further one: whole, in the second go.
    private IDrawingSession Text(Brush background, Size<double> desiredSize, Matrix4x4F transform,
        System.Func<bool, IDrawingSession> draw)
    {
        var first = !_textSeen;
        _textSeen = true;
        if (fromText)
        {
            draw(!first);
        }
        else if (first && background.IsVisible())
        {
            inner.DrawGeometry(background, new RectangleGeometry(new Rect(desiredSize)), null, transform);
        }

        return this;
    }

    public IDrawingSession DrawLine(Vector2<double> start, Vector2<double> end, Pen pen) =>
        Shape(() => inner.DrawLine(start, end, pen));

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, Pen pen = null) =>
        Shape(() => inner.DrawRectangle(brush, destinationRect, pen));

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen = null) =>
        Shape(() => inner.DrawRectangle(brush, destinationRect, corners, pen));

    public IDrawingSession DrawBorder(Brush background, Rect destinationRect, CornerRadius corners, Brush borderBrush,
        Thickness borderThickness) =>
        Shape(() => inner.DrawBorder(background, destinationRect, corners, borderBrush, borderThickness));

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, double startAngle, double sweepAngle,
        EllipseType ellipseType, Pen pen = null, double ringThickness = 0) =>
        Shape(() => inner.DrawEllipse(destinationRect, brush, startAngle, sweepAngle, ellipseType, pen, ringThickness));

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Pen pen = null,
        double ringThickness = 0, double startAngle = 0) =>
        Shape(() => inner.DrawRegularPolygon(brush, destinationRect, corners, pen, ringThickness, startAngle));

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen = null) =>
        Shape(() => inner.DrawGeometry(brush, geometry, pen));

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Matrix4x4F transform) =>
        Shape(() => inner.DrawGeometry(brush, geometry, pen, transform));

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Placement placement) =>
        Shape(() => inner.DrawGeometry(brush, geometry, pen, placement));

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen,
        Placement placement) => Shape(() => inner.DrawRectangle(brush, destinationRect, corners, pen, placement));

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, Pen pen, Placement placement) =>
        Shape(() => inner.DrawEllipse(destinationRect, brush, pen, placement));

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, double startAngle, Pen pen,
        Placement placement) =>
        Shape(() => inner.DrawRegularPolygon(brush, destinationRect, corners, startAngle, pen, placement));

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners) =>
        Shape(() => inner.DrawImage(image, filter, destinationRect, corners));

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        Rect sourceUv) => Shape(() => inner.DrawImage(image, filter, destinationRect, corners, sourceUv));

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4F transform) =>
        Shape(() => inner.DrawImage(image, filter, destinationRect, transform));

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer) => Shape(() => inner.DrawImageFrame(image, filter, destinationRect, corners, frameLayer));

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer, Rect sourceUv) =>
        Shape(() => inner.DrawImageFrame(image, filter, destinationRect, corners, frameLayer, sourceUv));

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke) =>
        Text(background, desiredSize, Matrix4x4F.Identity, whole =>
            inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, whole ? background : null, stroke));

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Matrix4x4F transform) =>
        Text(background, desiredSize, transform, whole =>
            inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, whole ? background : null, stroke,
                transform));

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Placement placement) =>
        Text(background, desiredSize, placement?.Matrix ?? Matrix4x4F.Identity, whole =>
            inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, whole ? background : null, stroke,
                placement));

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects) =>
        Text(background, desiredSize, Matrix4x4F.Identity, whole =>
            inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, whole ? background : null, stroke,
                effects));

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects,
        RectangleF window) =>
        Text(background, desiredSize, Matrix4x4F.Identity, whole =>
            inner.DrawText(renderingParameters, desiredSize, textLayout, foreground, whole ? background : null, stroke,
                effects, window));

    public IDrawingSession DrawGlyphs(GlyphRun run, Brush fill, Pen stroke, Matrix4x4F transform) =>
        Text(null, default, transform, _ => inner.DrawGlyphs(run, fill, stroke, transform));

    public IDrawingSession DrawColoredMesh(ColoredMesh mesh, Matrix4x4F transform) =>
        Shape(() => inner.DrawColoredMesh(mesh, transform));

    public IDrawingSession PushGroup(DrawingGroupOptions options)
    {
        inner.PushGroup(options);
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

    public bool DrawsGlyphEffects => inner.DrawsGlyphEffects;
}
