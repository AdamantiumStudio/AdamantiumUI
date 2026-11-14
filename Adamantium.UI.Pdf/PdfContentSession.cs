using System.Collections.Generic;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Writing;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf;

/// <summary>Writes what is drawn into a PDF content stream as paths - what an annotation's appearance is. Geometry is
/// written from the curves the GPU fills and strokes it by, in its own units under the matrix it is drawn with, so
/// widths, dashes and ends are the drawing's own; ink and arrows are filled as their passes shape them, each in one
/// piece; text by its glyphs' outlines. Solid brushes only: pictures, colored meshes and any other brush have no place
/// in such a drawing and are left out. A group's clip cuts what is drawn in it, and its opacity fades each thing drawn
/// in it rather than the group as one; its mask, text clip and blend mode are not written.</summary>
public sealed class PdfContentSession : IDrawingSession
{
    private const double Kappa = 0.5522847498307936;

    private readonly PdfContentBuilder _content;
    private readonly PdfMatrix _toPage;
    private readonly Stack<double> _groupOpacities = new();
    private double _opacity = 1;
    private double _left = double.PositiveInfinity;
    private double _bottom = double.PositiveInfinity;
    private double _right = double.NegativeInfinity;
    private double _top = double.NegativeInfinity;

    /// <param name="content">The content stream written into.</param>
    /// <param name="toPage">From the units things are drawn in to the page's: for a sheet, the inverse of
    /// <see cref="PdfPageSpace.ToSheet"/>.</param>
    public PdfContentSession(PdfContentBuilder content, PdfMatrix toPage)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _toPage = toPage;
    }

    /// <summary>The box of everything drawn so far, strokes included, in the page's space; empty while nothing is.
    /// </summary>
    public PdfRectangle Bounds => _left > _right ? default : new PdfRectangle(_left, _bottom, _right, _top);

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

    public IDrawingSession DrawBorder(Brush background, Rect destinationRect, CornerRadius corners, Brush borderBrush,
        Thickness borderThickness)
    {
        Figures(background, null, new RectangleGeometry(destinationRect, corners), Matrix4x4F.Identity);
        var width = borderThickness.Left;
        if (width > 0 && destinationRect.Width > width && destinationRect.Height > width)
        {
            var inside = new Rect(destinationRect.X + width / 2, destinationRect.Y + width / 2,
                destinationRect.Width - width, destinationRect.Height - width);
            Figures(null, new Pen(borderBrush, width), new RectangleGeometry(inside, corners), Matrix4x4F.Identity);
        }

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
            var whole = sweepAngle >= 360;
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

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners) => this;

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        Rect sourceUv) => this;

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4F transform) => this;

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer) => this;

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer, Rect sourceUv) => this;

    public IDrawingSession PushImage(ImageSource image) => this;

    public IDrawingSession DrawColoredMesh(ColoredMesh mesh, Matrix4x4F transform) => this;

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

    public IDrawingSession PushGroup(DrawingGroupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _content.SaveState();
        _groupOpacities.Push(_opacity);
        _opacity *= Math.Clamp(options.Opacity, 0, 1);
        if (options.Clip is { } clip && clip.CurvesForDrawing is { } curves)
        {
            var place = Through(options.ClipPlacement?.Matrix ?? options.ClipTransform);
            if (Write(curves, true, place))
            {
                _content.Clip(curves.FillRule == FillRule.EvenOdd);
            }
        }

        return this;
    }

    public IDrawingSession PopGroup()
    {
        if (_groupOpacities.Count == 0)
        {
            throw new InvalidOperationException("PopGroup without a group pushed.");
        }

        _opacity = _groupOpacities.Pop();
        _content.RestoreState();
        return this;
    }

    private void Figures(Brush brush, Pen pen, Geometry geometry, Matrix4x4F transform)
    {
        if (geometry?.CurvesForDrawing is not { Figures.Count: > 0 } curves)
        {
            return;
        }

        var fills = TrySolid(brush, out var fillColor, out var fillAlpha);
        var strokeColor = default(Color);
        var strokeAlpha = 0.0;
        var strokes = pen != null && pen.Thickness > 0 && TrySolid(pen.Brush, out strokeColor, out strokeAlpha);
        var place = Through(transform);
        if ((!fills && !strokes) || !Invertible(place))
        {
            return;
        }

        _content.SaveState();
        Opacity(strokes ? strokeAlpha : 1, fills ? fillAlpha : 1);
        if (fills)
        {
            _content.FillColor(fillColor.R / 255.0, fillColor.G / 255.0, fillColor.B / 255.0);
        }

        Transform(place);
        if (strokes)
        {
            Pen(pen, strokeColor);
        }

        if (Write(curves, fills && !strokes, PdfMatrix.Identity))
        {
            var evenOdd = curves.FillRule == FillRule.EvenOdd;
            if (fills && strokes)
            {
                _content.FillAndStroke(evenOdd);
            }
            else if (fills)
            {
                _content.Fill(evenOdd);
            }
            else
            {
                _content.Stroke();
            }

            var reach = strokes ? pen.Thickness / 2 * (pen.PenLineJoin == PenLineJoin.Miter
                ? MiterReach(curves, Core.Media.Pen.ClampMiterLimit(pen.MiterLimit))
                : 1) : 0;
            GrowBox(curves.Bounds, place, reach);
        }

        _content.RestoreState();
    }

    private void Ink(InkBrush ink, Matrix4x4F transform)
    {
        var points = ink.Points;
        var count = Math.Min(ink.Count, points?.Length ?? 0);
        var alpha = ink.Color.A / 255.0 * ink.Opacity * _opacity;
        var place = Through(transform);
        if (count == 0 || !(ink.Thickness > 0) || alpha <= 0 || !Invertible(place))
        {
            return;
        }

        _content.SaveState();
        var color = ink.Color;
        var widths = ink.Widths;
        if (widths == null)
        {
            Opacity(alpha, 1);
            _content.StrokeColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
            Transform(place);
            _content.LineWidth(ink.Thickness).LineCap(1).LineJoin(1);
            _content.MoveTo(points[0].X, points[0].Y);
            for (var index = 1; index < count; index++)
            {
                _content.LineTo(points[index].X, points[index].Y);
            }

            if (count == 1)
            {
                _content.LineTo(points[0].X, points[0].Y);
            }

            _content.Stroke();
            GrowPoints(points, count, place, ink.Thickness / 2);
            _content.RestoreState();
            return;
        }

        Opacity(1, alpha);
        _content.FillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
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

        _content.Fill();
        GrowPoints(points, count, place, widest);
        _content.RestoreState();
    }

    private void Arrow(CanvasArrowBrush arrow, Matrix4x4F transform)
    {
        var alpha = arrow.Color.A / 255.0 * arrow.Opacity * _opacity;
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

        _content.SaveState();
        Opacity(1, alpha);
        _content.FillColor(arrow.Color.R / 255.0, arrow.Color.G / 255.0, arrow.Color.B / 255.0);
        Transform(place);
        Capsule(shaftFrom, shaftTo, edge);
        Head(arrow.StartHead, from, along * -1, back, wide, edge);
        Head(arrow.EndHead, to, along, back, wide, edge);
        _content.Fill();
        var reach = Math.Max(edge, wide);
        GrowBox(new Rect(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Abs(to.X - from.X),
            Math.Abs(to.Y - from.Y)), place, reach);
        _content.RestoreState();
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
        if (layout == null || !TrySolid(foreground, out _, out _))
        {
            return;
        }

        var area = parameters?.TextArea ?? default;
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
                Run(font, size, glyphs, foreground, transform);
                font = word.Font;
                size = word.FontSize;
            }

            var box = glyph.BoundingRectangle;
            var scale = word.FontSize / word.Font.UnitsPerEm;
            glyphs.Add(new PositionedGlyph(glyph.Index, word.Rect.X + area.X - box.X * scale,
                word.Rect.Y + area.Y + (box.Y + box.Height) * scale));
        }

        Run(font, size, glyphs, foreground, transform);
    }

    private void Run(IFont font, double size, List<PositionedGlyph> glyphs, Brush foreground, Matrix4x4F transform)
    {
        if (font == null || glyphs.Count == 0)
        {
            glyphs.Clear();
            return;
        }

        Figures(foreground, null, new GlyphRun(font, size, glyphs).Outline, transform);
        glyphs.Clear();
    }

    private void Pen(Pen pen, Color color)
    {
        _content.StrokeColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        _content.LineWidth(pen.Thickness);
        _content.LineJoin(pen.PenLineJoin switch
        {
            PenLineJoin.Round => 1,
            PenLineJoin.Bevel => 2,
            _ => 0,
        });
        _content.LineCap(pen.StartLineCap switch
        {
            PenLineCap.ConvexRound => 1,
            PenLineCap.Square => 2,
            _ => 0,
        });
        _content.MiterLimit(Core.Media.Pen.ClampMiterLimit(pen.MiterLimit));
        if (pen.DashStrokeArray is { Count: > 0 } dashes)
        {
            _content.Dash([.. dashes], pen.DashOffset);
        }
    }

    private bool Write(PathCurves curves, bool filledOnly, PdfMatrix map)
    {
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
                _content.ClosePath();
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

        var hold = radius * Kappa;
        _content.MoveTo(center.X + radius, center.Y);
        _content.CurveTo(center.X + radius, center.Y + hold, center.X + hold, center.Y + radius, center.X, center.Y + radius);
        _content.CurveTo(center.X - hold, center.Y + radius, center.X - radius, center.Y + hold, center.X - radius, center.Y);
        _content.CurveTo(center.X - radius, center.Y - hold, center.X - hold, center.Y - radius, center.X, center.Y - radius);
        _content.CurveTo(center.X + hold, center.Y - radius, center.X + radius, center.Y - hold, center.X + radius, center.Y);
        _content.ClosePath();
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

        _content.MoveTo(points[0].X, points[0].Y);
        for (var index = 1; index < points.Length; index++)
        {
            _content.LineTo(points[index].X, points[index].Y);
        }

        _content.ClosePath();
    }

    private void Opacity(double stroke, double fill)
    {
        if (stroke < 1 || fill < 1)
        {
            _content.Opacity(stroke, fill);
        }
    }

    private void Transform(PdfMatrix place) => _content.Transform(place.A, place.B, place.C, place.D, place.E, place.F);

    private void MoveTo(PdfMatrix map, Vector2<double> point)
    {
        var at = map.Transform(point.X, point.Y);
        _content.MoveTo(at.X, at.Y);
    }

    private void LineTo(PdfMatrix map, Vector2<double> point)
    {
        var at = map.Transform(point.X, point.Y);
        _content.LineTo(at.X, at.Y);
    }

    private void CurveTo(PdfMatrix map, Vector2<double> first, Vector2<double> second, Vector2<double> end)
    {
        var one = map.Transform(first.X, first.Y);
        var two = map.Transform(second.X, second.Y);
        var three = map.Transform(end.X, end.Y);
        _content.CurveTo(one.X, one.Y, two.X, two.Y, three.X, three.Y);
    }

    private void GrowPoints(Vector2<float>[] points, int count, PdfMatrix place, double reach)
    {
        for (var index = 0; index < count; index++)
        {
            GrowBox(new Rect(points[index].X, points[index].Y, 0, 0), place, reach);
        }
    }

    private void GrowBox(Rect local, PdfMatrix place, double reach)
    {
        var inflated = new Rect(local.X - reach, local.Y - reach, local.Width + reach * 2, local.Height + reach * 2);
        Corner(place.Transform(inflated.X, inflated.Y));
        Corner(place.Transform(inflated.Right, inflated.Y));
        Corner(place.Transform(inflated.X, inflated.Bottom));
        Corner(place.Transform(inflated.Right, inflated.Bottom));
    }

    private void Corner(PdfPoint point)
    {
        _left = Math.Min(_left, point.X);
        _right = Math.Max(_right, point.X);
        _bottom = Math.Min(_bottom, point.Y);
        _top = Math.Max(_top, point.Y);
    }

    private PdfMatrix Through(Matrix4x4F transform) =>
        new PdfMatrix(transform.M11, transform.M12, transform.M21, transform.M22, transform.M41, transform.M42)
            .Then(_toPage);

    private bool TrySolid(Brush brush, out Color color, out double alpha)
    {
        if (brush is SolidColorBrush solid)
        {
            color = solid.Color;
            alpha = solid.Color.A / 255.0 * solid.Opacity * _opacity;
            return alpha > 0;
        }

        color = default;
        alpha = 0;
        return false;
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
}
