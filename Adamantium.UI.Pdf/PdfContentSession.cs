using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf;

/// <summary>Writes what is drawn into a PDF content stream - a page to print, or an annotation's appearance. Geometry
/// is written from the curves the GPU fills and strokes it by, in its own units under the matrix it is drawn with, so
/// widths, dashes and ends are the drawing's own; ink and arrows are filled as their passes shape them, each in one
/// piece; text by its glyphs' outlines. Brushes: solid, linear and radial gradients (their alpha a soft mask). Pictures
/// go in as images, a drawing's picture as the drawing itself; colored meshes as shaded triangles. A group is cut by
/// its clip and its glyphs; one faded, blended or masked is a transparency group, faded as one.
/// <para>Pens are stroked as the stroke shader lays them - trim, dashes fitted or not, every kind of end - and tile
/// brushes (a picture, a drawing) laid out as the renderer lays them, once or as tiling patterns, tinted.</para>
/// <para>Not written yet - the raster at print resolution is for them: a visual brush, a brush laid along a pen's line,
/// live surfaces; and a drawing tile's brightness, contrast and saturation.</para>
/// </summary>
public sealed partial class PdfContentSession : IDrawingSession
{
    private const double Kappa = 0.5522847498307936;

    private readonly PdfPictures _pictures;
    private readonly Level _root;
    private readonly Stack<Level> _levels = new();
    private Stack<PdfMatrix> _inners = new();

    /// <summary>From the drawing's root units to the content's: the page's - or, while a tile's cell is drawn, the cell's.
    /// </summary>
    private PdfMatrix _base;

    private PdfMatrix _through;

    /// <summary>Multiplied into every color written - a tinted drawing tile's tint, as the renderer multiplies it into
    /// every pixel; white leaves colors as they are.</summary>
    private Color _tint = Mathematics.Colors.White;

    /// <param name="content">The content stream written into.</param>
    /// <param name="toPage">From the units things are drawn in to the page's: for a sheet, the inverse of
    /// <see cref="PdfPageSpace.ToSheet"/>.</param>
    public PdfContentSession(PdfContentBuilder content, PdfMatrix toPage) : this(content, toPage, new PdfPictures())
    {
    }

    /// <param name="pictures">The pictures already turned into images - one for the pages of a document, so a picture
    /// on every page is written once.</param>
    internal PdfContentSession(PdfContentBuilder content, PdfMatrix toPage, PdfPictures pictures)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(pictures);
        _base = toPage;
        _pictures = pictures;
        _through = toPage;
        _root = new Level(content, null, false);
        _levels.Push(_root);
    }

    /// <summary>The box of everything drawn so far, strokes included, in the page's space; empty while nothing is.
    /// </summary>
    public PdfRectangle Bounds => _root.Box;

    private PdfContentBuilder Content => _levels.Peek().Builder;

    public IDrawingSession DrawLine(Vector2<double> start, Vector2<double> end, Pen pen)
    {
        var line = new StreamGeometry();
        var figure = line.Open().BeginFigure(start, false, false);
        figure.LineTo(end);
        line.RecalculateBounds();
        Figures(null, pen, line, Matrix4x4F.Identity);
        return this;
    }

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, Pen pen = null) =>
        DrawRectangle(brush, destinationRect, new CornerRadius(0), pen, null);

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen = null) =>
        DrawRectangle(brush, destinationRect, corners, pen, null);

    public IDrawingSession DrawRectangle(Brush brush, Rect destinationRect, CornerRadius corners, Pen pen,
        Placement placement)
    {
        var transform = placement?.Matrix ?? Matrix4x4F.Identity;
        switch (brush)
        {
            case InkBrush ink:
                Ink(ink, transform);
                return this;
            case CanvasArrowBrush arrow:
                Arrow(arrow, transform);
                return this;
            default:
                Figures(brush, pen, new RectangleGeometry(destinationRect, corners), transform);
                return this;
        }
    }

    /// <summary>The fill, then the border as the ring between the rect and the rect less each side's width - four
    /// widths, each corner rounded inside by what is left of its radius.</summary>
    public IDrawingSession DrawBorder(Brush background, Rect destinationRect, CornerRadius corners, Brush borderBrush,
        Thickness borderThickness)
    {
        Figures(background, null, new RectangleGeometry(destinationRect, corners), Matrix4x4F.Identity);
        var left = Math.Max(0, borderThickness.Left);
        var top = Math.Max(0, borderThickness.Top);
        var right = Math.Max(0, borderThickness.Right);
        var bottom = Math.Max(0, borderThickness.Bottom);
        if (left + top + right + bottom <= 0)
        {
            return this;
        }

        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new RectangleGeometry(destinationRect, corners));
        var width = destinationRect.Width - left - right;
        var height = destinationRect.Height - top - bottom;
        if (width > 0 && height > 0)
        {
            ring.Children.Add(new RectangleGeometry(new Rect(destinationRect.X + left, destinationRect.Y + top, width,
                height), new CornerRadius(
                Math.Max(0, corners.TopLeft - Math.Max(left, top)),
                Math.Max(0, corners.TopRight - Math.Max(right, top)),
                Math.Max(0, corners.BottomRight - Math.Max(right, bottom)),
                Math.Max(0, corners.BottomLeft - Math.Max(left, bottom)))));
        }

        ring.RecalculateBounds();
        Figures(borderBrush, null, ring, Matrix4x4F.Identity);
        return this;
    }

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, double startAngle, double sweepAngle,
        EllipseType ellipseType, Pen pen = null, double ringThickness = 0)
    {
        if (ringThickness > 0 && ringThickness * 2 < Math.Min(destinationRect.Width, destinationRect.Height))
        {
            var half = ringThickness / 2;
            var middle = new Rect(destinationRect.X + half, destinationRect.Y + half, destinationRect.Width - ringThickness,
                destinationRect.Height - ringThickness);
            var whole = new ArcSpan(startAngle, sweepAngle).IsWhole;
            Figures(null, new Pen(brush, ringThickness),
                new EllipseGeometry(middle, startAngle, sweepAngle, whole ? ellipseType : EllipseType.EdgeToEdge),
                Matrix4x4F.Identity);
            return this;
        }

        Figures(brush, pen, new EllipseGeometry(destinationRect, startAngle, sweepAngle, ellipseType),
            Matrix4x4F.Identity);
        return this;
    }

    public IDrawingSession DrawEllipse(Rect destinationRect, Brush brush, Pen pen, Placement placement)
    {
        Figures(brush, pen, new EllipseGeometry(destinationRect), placement?.Matrix ?? Matrix4x4F.Identity);
        return this;
    }

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, Pen pen = null,
        double ringThickness = 0, double startAngle = 0)
    {
        Figures(brush, pen, RegularPolygonGeometry.Build(destinationRect, corners, startAngle, ringThickness),
            Matrix4x4F.Identity);
        return this;
    }

    public IDrawingSession DrawRegularPolygon(Brush brush, Rect destinationRect, int corners, double startAngle, Pen pen,
        Placement placement)
    {
        Figures(brush, pen, RegularPolygonGeometry.Build(destinationRect, corners, startAngle),
            placement?.Matrix ?? Matrix4x4F.Identity);
        return this;
    }

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen = null) =>
        DrawGeometry(brush, geometry, pen, Matrix4x4F.Identity);

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Matrix4x4F transform)
    {
        Figures(brush, pen, geometry, transform);
        return this;
    }

    public IDrawingSession DrawGeometry(Brush brush, Geometry geometry, Pen pen, Placement placement) =>
        DrawGeometry(brush, geometry, pen, placement?.Matrix ?? Matrix4x4F.Identity);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke) =>
        DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke, Matrix4x4F.Identity);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Matrix4x4F transform)
    {
        Text(renderingParameters, textLayout, foreground, transform);
        return this;
    }

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, Placement placement) =>
        DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke,
            placement?.Matrix ?? Matrix4x4F.Identity);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects) =>
        DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke, Matrix4x4F.Identity);

    public IDrawingSession DrawText(TextRenderingParameters renderingParameters, Size<double> desiredSize,
        TextLayout textLayout, Brush foreground, Brush background, Brush stroke, IReadOnlyList<GlyphEffect> effects,
        RectangleF window) =>
        DrawText(renderingParameters, desiredSize, textLayout, foreground, background, stroke, Matrix4x4F.Identity);

    public IDrawingSession DrawGlyphs(GlyphRun run, Brush fill, Pen stroke, Matrix4x4F transform)
    {
        ArgumentNullException.ThrowIfNull(run);
        Figures(fill, stroke, run.Outline, transform);
        return this;
    }

    /// <summary>Cut by the clip and the glyphs in place; faded, blended or masked, the group is drawn into a
    /// transparency group of its own and laid down as one at <see cref="PopGroup"/>.</summary>
    public IDrawingSession PushGroup(DrawingGroupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var apart = Math.Clamp(options.Opacity, 0, 1) < 1 || options.BlendMode != BlendMode.Normal ||
                    options.Mask != null;
        if (apart)
        {
            _levels.Push(new Level(new PdfContentBuilder(), options, true));
        }
        else
        {
            Content.SaveState();
            _levels.Push(new Level(Content, options, false));
        }

        if (options.Clip is { } clip)
        {
            // A clip with nothing in it lets nothing through, as on screen.
            var curves = clip.CurvesForDrawing;
            var place = Through(options.ClipPlacement?.Matrix ?? options.ClipTransform);
            if (curves == null || !Write(curves, true, place))
            {
                Content.Rectangle(0, 0, 0, 0);
            }

            Content.Clip(curves?.FillRule == FillRule.EvenOdd);
        }

        if (options.TextClip is { Count: > 0 } glyphs)
        {
            var wrote = false;
            foreach (var placed in glyphs)
            {
                if (placed?.Run?.Outline?.CurvesForDrawing is { } outline)
                {
                    wrote |= Write(outline, false, Through(placed.Transform));
                }
            }

            if (!wrote)
            {
                Content.Rectangle(0, 0, 0, 0);
            }

            Content.Clip();
        }

        return this;
    }

    public IDrawingSession PopGroup()
    {
        if (_levels.Count <= 1)
        {
            throw new InvalidOperationException("PopGroup without a group pushed.");
        }

        if (_levels.Peek().Options == null)
        {
            throw new InvalidOperationException("PopGroup past the groups a soft mask's picture pushed.");
        }

        var level = _levels.Pop();
        if (!level.IsApart)
        {
            Content.RestoreState();
            _levels.Peek().Take(level);
            return this;
        }

        if (level.IsEmpty)
        {
            return this;
        }

        var options = level.Options;
        var box = level.Box;
        var form = level.Builder.ToFormXObject(box, PdfTransparencyGroup.Layer);
        var mask = options.Mask != null ? MaskOf(options, box) : null;
        var content = Content;
        content.SaveState();
        if (mask != null)
        {
            content.SoftMask(mask, options.MaskKind == MaskKind.Luminosity);
        }

        var opacity = Math.Clamp(options.Opacity, 0, 1);
        if (opacity < 1)
        {
            content.Opacity(opacity, opacity);
        }

        if (options.BlendMode != BlendMode.Normal)
        {
            content.BlendMode(ToPdf(options.BlendMode));
        }

        content.Draw(form);
        content.RestoreState();
        _levels.Peek().Take(level);
        return this;
    }

    /// <summary>Ends the groups left open, as a drawing's end ends them on screen. A group faded, blended or masked is
    /// written into the content only as it ends: call this before the content or <see cref="Bounds"/> is used.</summary>
    public void CloseGroups()
    {
        while (_levels.Count > 1)
        {
            PopGroup();
        }
    }

    /// <summary>The group's soft mask: its picture drawn into a group of its own - over its backdrop color, for a mask
    /// read by luminosity - in the space the group is laid down in.</summary>
    private PdfStream MaskOf(DrawingGroupOptions options, PdfRectangle box)
    {
        var level = new Level(new PdfContentBuilder(), null, true);
        if (options.MaskKind == MaskKind.Luminosity)
        {
            var backdrop = options.MaskBackdrop;
            level.Builder.FillColor(backdrop.R / 255.0, backdrop.G / 255.0, backdrop.B / 255.0)
                .Rectangle(box.Left, box.Bottom, box.Width, box.Height).Fill();
        }

        // A mask says how much shows, not what color: no tint in it.
        var tint = _tint;
        _tint = Mathematics.Colors.White;
        _levels.Push(level);
        try
        {
            options.Mask.Render(this, options.MaskTransform, null);
        }
        finally
        {
            while (_levels.Pop() != level)
            {
            }

            _tint = tint;
        }

        return level.Builder.ToFormXObject(box, PdfTransparencyGroup.Layer);
    }

    private static PdfBlendMode ToPdf(BlendMode mode) => mode switch
    {
        BlendMode.Multiply => PdfBlendMode.Multiply,
        BlendMode.Screen => PdfBlendMode.Screen,
        BlendMode.Overlay => PdfBlendMode.Overlay,
        BlendMode.Darken => PdfBlendMode.Darken,
        BlendMode.Lighten => PdfBlendMode.Lighten,
        BlendMode.ColorDodge => PdfBlendMode.ColorDodge,
        BlendMode.ColorBurn => PdfBlendMode.ColorBurn,
        BlendMode.HardLight => PdfBlendMode.HardLight,
        BlendMode.SoftLight => PdfBlendMode.SoftLight,
        BlendMode.Difference => PdfBlendMode.Difference,
        BlendMode.Exclusion => PdfBlendMode.Exclusion,
        BlendMode.Hue => PdfBlendMode.Hue,
        BlendMode.Saturation => PdfBlendMode.Saturation,
        BlendMode.Color => PdfBlendMode.Color,
        BlendMode.Luminosity => PdfBlendMode.Luminosity,
        _ => PdfBlendMode.Normal,
    };

    private void Ink(InkBrush ink, Matrix4x4F transform)
    {
        var points = ink.Points;
        var count = Math.Min(ink.Count, points?.Length ?? 0);
        var alpha = ink.Color.A / 255.0 * ink.Opacity;
        var place = Through(transform);
        if (count == 0 || !(ink.Thickness > 0) || alpha <= 0 || !Invertible(place))
        {
            return;
        }

        var content = Content;
        content.SaveState();
        var color = ink.Color;
        var widths = ink.Widths;
        if (widths == null)
        {
            Opacity(alpha, 1);
            StrokeColor(color);
            Transform(place);
            content.LineWidth(ink.Thickness).LineCap(1).LineJoin(1);
            content.MoveTo(points[0].X, points[0].Y);
            for (var index = 1; index < count; index++)
            {
                content.LineTo(points[index].X, points[index].Y);
            }

            if (count == 1)
            {
                content.LineTo(points[0].X, points[0].Y);
            }

            content.Stroke();
            GrowPoints(points, count, place, ink.Thickness / 2);
            content.RestoreState();
            return;
        }

        Opacity(1, alpha);
        FillColor(color);
        Transform(place);
        var widest = 0.0;
        for (var index = 0; index < count; index++)
        {
            var radius = ink.Thickness * Share(widths, index) / 2;
            widest = Math.Max(widest, radius);
            Circle(At(points[index]), radius);
            if (index == 0)
            {
                continue;
            }

            var from = At(points[index - 1]);
            var to = At(points[index]);
            var side = Across(to - from);
            var before = ink.Thickness * Share(widths, index - 1) / 2;
            Polygon([from + side * before, to + side * radius, to - side * radius, from - side * before], false);
        }

        content.Fill();
        GrowPoints(points, count, place, widest);
        content.RestoreState();
    }

    private void Arrow(CanvasArrowBrush arrow, Matrix4x4F transform)
    {
        var alpha = arrow.Color.A / 255.0 * arrow.Opacity;
        var place = Through(transform);
        if (!(arrow.Thickness > 0) || alpha <= 0 || !Invertible(place))
        {
            return;
        }

        var from = arrow.From;
        var to = arrow.To;
        var span = Math.Max(Length(to - from), 1e-6);
        var along = (to - from) * (1 / span);
        var edge = arrow.Thickness / 2;
        var wide = arrow.HeadWidth;
        var length = arrow.HeadLength;
        var back = Math.Min(length, span);
        var tuck = edge / Math.Max(wide / Math.Sqrt(Math.Max(length * length + wide * wide, 1e-12)), 1e-6);
        var shaftFrom = arrow.StartHead >= 1 ? from + along * Math.Min(arrow.StartHead >= 2 ? length : tuck, span) : from;
        var shaftTo = arrow.EndHead >= 1 ? to - along * Math.Min(arrow.EndHead >= 2 ? length : tuck, span) : to;

        var content = Content;
        content.SaveState();
        Opacity(1, alpha);
        FillColor(arrow.Color);
        Transform(place);
        Capsule(shaftFrom, shaftTo, edge);
        Head(arrow.StartHead, from, along * -1, back, wide, edge);
        Head(arrow.EndHead, to, along, back, wide, edge);
        content.Fill();
        var reach = Math.Max(edge, wide);
        GrowBox(new Rect(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Abs(to.X - from.X),
            Math.Abs(to.Y - from.Y)), place, reach);
        content.RestoreState();
    }

    private void Head(int kind, Vector2<double> tip, Vector2<double> forward, double back, double wide, double edge)
    {
        if (kind < 1 || !(back > 0) || !(wide > 0))
        {
            return;
        }

        var side = Across(forward);
        var bottom = tip - forward * back;
        Polygon([tip, bottom + side * wide, bottom - side * wide], false);
        if (kind >= 2)
        {
            return;
        }

        var slant = 1 / Math.Sqrt(back * back + wide * wide);
        var apex = edge / (wide * slant);
        var inner = wide - edge / (slant * back);
        if (apex < back && inner > 0)
        {
            var notch = tip - forward * apex;
            Polygon([notch, bottom + side * inner, bottom - side * inner], true);
        }
    }

    private void Text(TextRenderingParameters parameters, TextLayout layout, Brush foreground, Matrix4x4F transform)
    {
        if (layout == null || !Paints(foreground))
        {
            return;
        }

        var area = parameters?.TextArea ?? default;
        var runs = new List<GlyphRun>();
        var glyphs = new List<PositionedGlyph>();
        IFont font = null;
        var size = 0.0;
        foreach (var word in layout.GetTextData())
        {
            var glyph = word.Glyph;
            if (glyph == null || word.Font == null || !glyph.HasOutlines || word.Rect.Width <= 0 || !(word.FontSize > 0))
            {
                continue;
            }

            if (!ReferenceEquals(word.Font, font) || word.FontSize != size)
            {
                Run(font, size, glyphs, runs);
                font = word.Font;
                size = word.FontSize;
            }

            var box = glyph.BoundingRectangle;
            var scale = word.FontSize / word.Font.UnitsPerEm;
            glyphs.Add(new PositionedGlyph(glyph.Index, word.Rect.X + area.X - box.X * scale,
                word.Rect.Y + area.Y + (box.Y + box.Height) * scale));
        }

        Run(font, size, glyphs, runs);

        // One gradient or tile across the whole text, however many fonts and sizes it is set in.
        Rect? whole = null;
        if (foreground is GradientBrush or TileBrush)
        {
            foreach (var run in runs)
            {
                if (run.Outline?.CurvesForDrawing is { Figures.Count: > 0 } curves)
                {
                    whole = whole is { } sofar ? Union(sofar, curves.Bounds) : curves.Bounds;
                }
            }
        }

        foreach (var run in runs)
        {
            Figures(foreground, null, run.Outline, transform, whole);
        }
    }

    private static void Run(IFont font, double size, List<PositionedGlyph> glyphs, List<GlyphRun> runs)
    {
        if (font != null && glyphs.Count > 0)
        {
            runs.Add(new GlyphRun(font, size, [.. glyphs]));
        }

        glyphs.Clear();
    }

    private static Rect Union(Rect one, Rect other)
    {
        var left = Math.Min(one.X, other.X);
        var top = Math.Min(one.Y, other.Y);
        return new Rect(left, top, Math.Max(one.Right, other.Right) - left, Math.Max(one.Bottom, other.Bottom) - top);
    }

    private bool Write(PathCurves curves, bool filledOnly, PdfMatrix map)
    {
        var content = Content;
        var wrote = false;
        foreach (var figure in curves.Figures)
        {
            if (figure.Count == 0 || (filledOnly && !figure.IsFilled))
            {
                continue;
            }

            var first = curves.Curves[figure.First];
            MoveTo(map, first.Start);
            for (var index = figure.First; index < figure.First + figure.Count; index++)
            {
                var curve = curves.Curves[index];
                if (curve.IsStraight())
                {
                    LineTo(map, curve.End);
                    continue;
                }

                CurveTo(map, curve.Start + (curve.Control - curve.Start) * (2.0 / 3),
                    curve.End + (curve.Control - curve.End) * (2.0 / 3), curve.End);
            }

            if (figure.IsClosed)
            {
                content.ClosePath();
            }

            wrote = true;
        }

        return wrote;
    }

    private void Capsule(Vector2<double> from, Vector2<double> to, double radius)
    {
        if (!(radius > 0))
        {
            return;
        }

        Circle(from, radius);
        if (Length(to - from) <= 1e-9)
        {
            return;
        }

        var side = Across(to - from);
        Polygon([from + side * radius, to + side * radius, to - side * radius, from - side * radius], false);
        Circle(to, radius);
    }

    private void Circle(Vector2<double> center, double radius)
    {
        if (!(radius > 0))
        {
            return;
        }

        var content = Content;
        var hold = radius * Kappa;
        content.MoveTo(center.X + radius, center.Y);
        content.CurveTo(center.X + radius, center.Y + hold, center.X + hold, center.Y + radius, center.X, center.Y + radius);
        content.CurveTo(center.X - hold, center.Y + radius, center.X - radius, center.Y + hold, center.X - radius, center.Y);
        content.CurveTo(center.X - radius, center.Y - hold, center.X - hold, center.Y - radius, center.X, center.Y - radius);
        content.CurveTo(center.X + hold, center.Y - radius, center.X + radius, center.Y - hold, center.X + radius, center.Y);
        content.ClosePath();
    }

    private void Polygon(Vector2<double>[] points, bool hole)
    {
        var area = 0.0;
        for (var index = 0; index < points.Length; index++)
        {
            var next = points[(index + 1) % points.Length];
            area += points[index].X * next.Y - next.X * points[index].Y;
        }

        if ((area < 0) != hole)
        {
            Array.Reverse(points);
        }

        var content = Content;
        content.MoveTo(points[0].X, points[0].Y);
        for (var index = 1; index < points.Length; index++)
        {
            content.LineTo(points[index].X, points[index].Y);
        }

        content.ClosePath();
    }

    /// <summary><paramref name="color"/> with the tint multiplied into its red, green and blue; its alpha as it is - a
    /// tint's own alpha fades the whole tile.</summary>
    private Color Tinted(Color color) => _tint == Mathematics.Colors.White
        ? color
        : new Color((byte)Math.Round(color.R * _tint.R / 255.0), (byte)Math.Round(color.G * _tint.G / 255.0),
            (byte)Math.Round(color.B * _tint.B / 255.0), color.A);

    private void FillColor(Color color)
    {
        var tinted = Tinted(color);
        Content.FillColor(tinted.R / 255.0, tinted.G / 255.0, tinted.B / 255.0);
    }

    private void StrokeColor(Color color)
    {
        var tinted = Tinted(color);
        Content.StrokeColor(tinted.R / 255.0, tinted.G / 255.0, tinted.B / 255.0);
    }

    private void Opacity(double stroke, double fill)
    {
        if (stroke < 1 || fill < 1)
        {
            Content.Opacity(stroke, fill);
        }
    }

    private void Transform(PdfMatrix place) => Content.Transform(place.A, place.B, place.C, place.D, place.E, place.F);

    private void MoveTo(PdfMatrix map, Vector2<double> point)
    {
        var at = map.Transform(point.X, point.Y);
        Content.MoveTo(at.X, at.Y);
    }

    private void LineTo(PdfMatrix map, Vector2<double> point)
    {
        var at = map.Transform(point.X, point.Y);
        Content.LineTo(at.X, at.Y);
    }

    private void CurveTo(PdfMatrix map, Vector2<double> first, Vector2<double> second, Vector2<double> end)
    {
        var one = map.Transform(first.X, first.Y);
        var two = map.Transform(second.X, second.Y);
        var three = map.Transform(end.X, end.Y);
        Content.CurveTo(one.X, one.Y, two.X, two.Y, three.X, three.Y);
    }

    private void GrowPoints(Vector2<float>[] points, int count, PdfMatrix place, double reach)
    {
        for (var index = 0; index < count; index++)
        {
            GrowBox(new Rect(points[index].X, points[index].Y, 0, 0), place, reach);
        }
    }

    private void GrowBox(Rect local, PdfMatrix place, double reach) => _levels.Peek().Grow(Box(local, place, reach));

    /// <summary>The page-space box of <paramref name="local"/> grown by <paramref name="reach"/> and placed.</summary>
    private static PdfRectangle Box(Rect local, PdfMatrix place, double reach)
    {
        var inflated = new Rect(local.X - reach, local.Y - reach, local.Width + reach * 2, local.Height + reach * 2);
        var one = place.Transform(inflated.X, inflated.Y);
        var two = place.Transform(inflated.Right, inflated.Y);
        var three = place.Transform(inflated.X, inflated.Bottom);
        var four = place.Transform(inflated.Right, inflated.Bottom);
        return new PdfRectangle(Math.Min(Math.Min(one.X, two.X), Math.Min(three.X, four.X)),
            Math.Min(Math.Min(one.Y, two.Y), Math.Min(three.Y, four.Y)),
            Math.Max(Math.Max(one.X, two.X), Math.Max(three.X, four.X)),
            Math.Max(Math.Max(one.Y, two.Y), Math.Max(three.Y, four.Y)));
    }

    private PdfMatrix Through(Matrix4x4F transform) => Matrix(transform).Then(_through);

    private static PdfMatrix Matrix(Matrix4x4F transform) =>
        new(transform.M11, transform.M12, transform.M21, transform.M22, transform.M41, transform.M42);

    /// <summary>Draws what follows as if placed by <paramref name="transform"/> too - a drawing's picture replayed
    /// where it is drawn - until <see cref="PopInner"/>.</summary>
    private void PushInner(Matrix4x4F transform)
    {
        var inner = _inners.Count > 0 ? _inners.Peek() : PdfMatrix.Identity;
        inner = Matrix(transform).Then(inner);
        _inners.Push(inner);
        _through = inner.Then(_base);
    }

    private void PopInner()
    {
        _inners.Pop();
        _through = (_inners.Count > 0 ? _inners.Peek() : PdfMatrix.Identity).Then(_base);
    }

    /// <summary>What <paramref name="draw"/> draws, written into content of its own in the units it draws in - a tile's
    /// cell - rather than into the page.</summary>
    private PdfContentBuilder Capture(Action draw)
    {
        var savedBase = _base;
        var savedInners = _inners;
        var savedThrough = _through;
        var level = new Level(new PdfContentBuilder(), null, true);
        _base = PdfMatrix.Identity;
        _inners = new Stack<PdfMatrix>();
        _through = PdfMatrix.Identity;
        _levels.Push(level);
        try
        {
            draw();
        }
        finally
        {
            while (_levels.Pop() != level)
            {
            }

            _base = savedBase;
            _inners = savedInners;
            _through = savedThrough;
        }

        return level.Builder;
    }

    private static double MiterReach(PathCurves curves, double limit)
    {
        var reach = 1.0;
        foreach (var figure in curves.Figures)
        {
            var last = figure.First + figure.Count - 1;
            for (var index = figure.First; index <= last; index++)
            {
                if (index == last && !figure.IsClosed)
                {
                    break;
                }

                var next = index == last ? figure.First : index + 1;
                var incoming = Leaving(curves.Curves[index], false);
                var outgoing = Leaving(curves.Curves[next], true);
                var turn = incoming.X * outgoing.X + incoming.Y * outgoing.Y;
                var halfAngleSine = Math.Sqrt(Math.Max(0, (1 - turn) / 2));
                var ratio = halfAngleSine > 1e-9 ? 1 / halfAngleSine : double.PositiveInfinity;
                if (ratio <= limit)
                {
                    reach = Math.Max(reach, ratio);
                }
            }
        }

        return reach;
    }

    private static Vector2<double> Leaving(QuadraticCurve curve, bool atStart)
    {
        var along = atStart
            ? (curve.IsStraight() ? curve.End : curve.Control) - curve.Start
            : curve.End - (curve.IsStraight() ? curve.Start : curve.Control);
        var length = Length(along);
        var unit = length > 0 ? along * (1 / length) : default;
        return atStart ? unit : unit * -1;
    }

    private static bool Invertible(PdfMatrix place) => Math.Abs(place.A * place.D - place.B * place.C) > 1e-12;

    private static double Length(Vector2<double> vector) => Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);

    private static Vector2<double> Across(Vector2<double> along)
    {
        var length = Length(along);
        return length > 0 ? new Vector2<double>(-along.Y / length, along.X / length) : default;
    }

    private static Vector2<double> At(Vector2<float> point) => new(point.X, point.Y);

    private static double Share(float[] widths, int index) => index < widths.Length ? Math.Max(0, widths[index]) : 1;

    /// <summary>A group being written: the content it goes into - its own for a group drawn apart - and the box of
    /// what it drew, in the page's space.</summary>
    private sealed class Level(PdfContentBuilder builder, DrawingGroupOptions options, bool isApart)
    {
        private double _left = double.PositiveInfinity;
        private double _bottom = double.PositiveInfinity;
        private double _right = double.NegativeInfinity;
        private double _top = double.NegativeInfinity;

        public PdfContentBuilder Builder { get; } = builder;

        public DrawingGroupOptions Options { get; } = options;

        public bool IsApart { get; } = isApart;

        public bool IsEmpty => _left > _right;

        public PdfRectangle Box => IsEmpty ? default : new PdfRectangle(_left, _bottom, _right, _top);

        public void Grow(PdfRectangle box)
        {
            _left = Math.Min(_left, box.Left);
            _bottom = Math.Min(_bottom, box.Bottom);
            _right = Math.Max(_right, box.Right);
            _top = Math.Max(_top, box.Top);
        }

        public void Take(Level inner)
        {
            if (!inner.IsEmpty)
            {
                Grow(inner.Box);
            }
        }
    }
}
