using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;
using Adamantium.Win32.Printing.Xps;
using static Adamantium.UI.Platforms.Windows.Printing.XpsGeometryPath;

namespace Adamantium.UI.Platforms.Windows.Printing;

/// <summary>Writes what is drawn into an XPS page through the XPS object model - a sheet to print. The page's units are
/// the drawing's, DIPs with y down, so a sheet goes in as it is drawn. Geometry is written from the curves the GPU fills
/// and strokes it by; text by its glyphs' outlines; ink and arrows as their passes shape them. Brushes: solid, linear and
/// radial gradients with their stops' alpha, pictures and drawings as tiles. A group is a canvas, cut by its clip and its
/// glyphs, faded and masked.
/// <para>A pen XPS strokes as it is - one cap at each end and one for the dashes, of XPS's kinds - is XPS's own;
/// any other is stroked as the stroke shader lays it (<see cref="StrokeRuns"/>), as in PDF.</para>
/// <para>Not written yet - the raster at print resolution is for them: blend modes and luminosity masks (a mask is read
/// by its alpha), a visual brush, a brush laid along a pen's line, live surfaces, a mesh's colors between its corners
/// (each small triangle takes the color at its middle), and a drawing tile's brightness, contrast and saturation.
/// </para></summary>
internal sealed partial class XpsDrawingSession : IDrawingSession
{
    private static readonly Rect WholePicture = new(0, 0, 1, 1);

    private readonly IXpsOMObjectFactory _factory;
    private readonly XpsPictures _pictures;
    private readonly XpsFonts _fonts;
    private readonly Stack<Level> _levels = new();

    /// <summary>Multiplied into every color written - a tinted drawing tile's tint; white leaves colors as they are.
    /// </summary>
    private Color _tint = Mathematics.Colors.White;

    /// <summary>Whether a pen's own alpha is laid by a canvas around its runs: its paint is then written opaque.</summary>
    private bool _alphaApart;

    /// <param name="visuals">The visuals of the page written into.</param>
    public XpsDrawingSession(IXpsOMObjectFactory factory, XpsPictures pictures, XpsFonts fonts,
        IXpsOMVisualCollection visuals)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(visuals);
        _factory = factory;
        _pictures = pictures;
        _fonts = fonts;
        _levels.Push(new Level(visuals, null, Matrix4x4F.Identity, owned: false));
    }

    /// <summary>A canvas being written into: its visuals, the group it is (null for the page and for a capture), and
    /// the matrix from its units to the page's.</summary>
    private sealed class Level(IXpsOMVisualCollection visuals, DrawingGroupOptions options, Matrix4x4F toPage,
        bool owned)
    {
        public IXpsOMVisualCollection Visuals { get; } = visuals;

        public DrawingGroupOptions Options { get; } = options;

        public Matrix4x4F ToPage { get; } = toPage;

        /// <summary>Whether the visuals are this level's to release - a canvas's, not the page's.</summary>
        public bool Owned { get; } = owned;
    }

    private Level Current => _levels.Peek();

    public IDrawingSession DrawLine(Vector2<double> start, Vector2<double> end, Pen pen)
    {
        var line = new StreamGeometry();
        line.Open().BeginFigure(start, false, false).LineTo(end);
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
        if (BorderRing.Of(destinationRect, corners, borderThickness) is { } ring)
        {
            Figures(borderBrush, null, ring, Matrix4x4F.Identity);
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
        // The text's own background, under it where the run is, as the screen lays it down.
        if (background.IsVisible())
        {
            DrawGeometry(background, new RectangleGeometry(new Rect(desiredSize)), null, transform);
        }

        if (textLayout != null)
        {
            Text(renderingParameters, textLayout, foreground, transform);
        }

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

    /// <summary>A canvas: cut by the clip - and, in a canvas within it, by the glyphs - faded by the opacity, masked by
    /// the mask's alpha.</summary>
    public IDrawingSession PushGroup(DrawingGroupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var level = Current;
        var outer = NewCanvas(level.Visuals);
        try
        {
            if (options.Clip is { } clip)
            {
                var path = new XpsGeometryPath();
                var curves = clip.CurvesForDrawing;
                if (curves != null)
                {
                    VectorShapes.AddCurves(path, curves, true);
                }

                // A clip with nothing in it lets nothing through, as on screen.
                SetClip(outer, path, curves?.FillRule == FillRule.EvenOdd,
                    options.ClipPlacement?.Matrix ?? options.ClipTransform);
            }

            var opacity = Math.Clamp(options.Opacity, 0, 1);
            if (opacity < 1)
            {
                Check(outer.SetOpacity((float)opacity));
            }

            if (options.Mask != null && MaskOf(options, level.ToPage) is { } mask)
            {
                try
                {
                    Check(outer.SetOpacityMaskBrushLocal(mask));
                }
                finally
                {
                    Marshal.ReleaseComObject(mask);
                }
            }

            Check(outer.GetVisuals(out var visuals));
            var pushed = new Level(visuals, options, level.ToPage, owned: true);
            _levels.Push(pushed);
            if (options.TextClip is { Count: > 0 } glyphs)
            {
                var inner = NewCanvas(visuals);
                try
                {
                    var path = new XpsGeometryPath();
                    foreach (var placed in glyphs)
                    {
                        if (placed?.Run?.Outline?.CurvesForDrawing is { } outline)
                        {
                            VectorShapes.AddCurves(new TransformedPath(path, placed.Transform), outline, false);
                        }
                    }

                    SetClip(inner, path, false, Matrix4x4F.Identity);
                    Check(inner.GetVisuals(out var innerVisuals));
                    _levels.Pop();
                    _levels.Push(new Level(innerVisuals, options, level.ToPage, owned: true));
                    Marshal.ReleaseComObject(visuals);
                }
                finally
                {
                    Marshal.ReleaseComObject(inner);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(outer);
        }

        return this;
    }

    public IDrawingSession PopGroup()
    {
        if (_levels.Count <= 1 || Current.Options == null)
        {
            throw new InvalidOperationException("PopGroup without a group pushed.");
        }

        var level = _levels.Pop();
        if (level.Owned)
        {
            Marshal.ReleaseComObject(level.Visuals);
        }

        return this;
    }

    /// <summary>Ends the groups left open, as a drawing's end ends them on screen.</summary>
    public void CloseGroups()
    {
        while (_levels.Count > 1 && Current.Options != null)
        {
            PopGroup();
        }
    }

    /// <summary>The group's opacity mask: the mask's picture drawn into a canvas of its own, as a visual brush over the
    /// box it covers. A mask read by luminosity is read by its alpha here - XPS has no other; the raster is for it.
    /// </summary>
    private IXpsOMBrush MaskOf(DrawingGroupOptions options, Matrix4x4F toPage)
    {
        var bounds = options.Mask.Bounds;
        if (!(bounds.Width > 0) || !(bounds.Height > 0))
        {
            return null;
        }

        var box = Across(bounds, options.MaskTransform);
        Check(_factory.CreateCanvas(out var canvas));
        try
        {
            Check(canvas.GetVisuals(out var visuals));
            var tint = _tint;
            _tint = Mathematics.Colors.White;
            _levels.Push(new Level(visuals, null, toPage, owned: true));
            try
            {
                options.Mask.Render(this, options.MaskTransform, null);
            }
            finally
            {
                _tint = tint;
                while (_levels.Peek().Visuals != visuals)
                {
                    PopLevel();
                }

                PopLevel();
            }

            var rect = new XpsRect((float)box.X, (float)box.Y, (float)box.Width, (float)box.Height);
            Check(_factory.CreateVisualBrush(rect, rect, out var brush));
            Check(brush.SetTileMode(XpsTileMode.None));
            Check(brush.SetVisualLocal((IXpsOMVisual)canvas));
            return (IXpsOMBrush)brush;
        }
        finally
        {
            Marshal.ReleaseComObject(canvas);
        }
    }

    private void PopLevel()
    {
        var level = _levels.Pop();
        if (level.Owned)
        {
            Marshal.ReleaseComObject(level.Visuals);
        }
    }

    /// <summary>A new canvas appended to <paramref name="visuals"/> - drawn where it stands among them.</summary>
    private IXpsOMCanvas NewCanvas(IXpsOMVisualCollection visuals)
    {
        Check(_factory.CreateCanvas(out var canvas));
        Check(visuals.Append((IXpsOMVisual)canvas));
        return canvas;
    }

    /// <summary>Cuts <paramref name="canvas"/> to <paramref name="path"/> placed by <paramref name="transform"/>; to
    /// nothing when the path is empty.</summary>
    private void SetClip(IXpsOMCanvas canvas, XpsGeometryPath path, bool evenOdd, Matrix4x4F transform)
    {
        if (path.IsEmpty)
        {
            path.MoveTo(default);
            path.LineTo(default);
            path.Close();
        }

        var geometry = path.ToGeometry(_factory, evenOdd);
        try
        {
            SetTransform(geometry, transform);
            Check(canvas.SetClipGeometryLocal(geometry));
        }
        finally
        {
            Marshal.ReleaseComObject(geometry);
        }
    }

    private void SetTransform(IXpsOMGeometry geometry, Matrix4x4F transform)
    {
        if (IsIdentity(transform))
        {
            return;
        }

        Check(_factory.CreateMatrixTransform(ToXps(transform), out var matrix));
        try
        {
            Check(geometry.SetTransformLocal(matrix));
        }
        finally
        {
            Marshal.ReleaseComObject(matrix);
        }
    }

    private void Ink(InkBrush ink, Matrix4x4F transform)
    {
        var count = Math.Min(ink.Count, ink.Points?.Length ?? 0);
        var alpha = ink.Color.A / 255.0 * ink.Opacity;
        if (count == 0 || !(ink.Thickness > 0) || alpha <= 0 || !Invertible(transform))
        {
            return;
        }

        var color = new SolidColorBrush(new Color(ink.Color.R, ink.Color.G, ink.Color.B, (byte)255)) { Opacity = alpha };
        var path = new XpsGeometryPath();
        if (ink.Widths == null)
        {
            // One width: the line stroked round, as its pass draws it.
            path.MoveTo(new Vector2<double>(ink.Points[0].X, ink.Points[0].Y));
            for (var index = count == 1 ? 0 : 1; index < count; index++)
            {
                path.LineTo(new Vector2<double>(ink.Points[index].X, ink.Points[index].Y));
            }

            WritePath(path, false, transform, null, null, color, ink.Thickness, XpsLineCap.Round, XpsLineJoin.Round);
            return;
        }

        VectorShapes.AddInk(path, ink, count);
        WritePath(path, false, transform, color, null, null, 0, XpsLineCap.Flat, XpsLineJoin.Miter);
    }

    private void Arrow(CanvasArrowBrush arrow, Matrix4x4F transform)
    {
        var alpha = arrow.Color.A / 255.0 * arrow.Opacity;
        if (!(arrow.Thickness > 0) || alpha <= 0 || !Invertible(transform))
        {
            return;
        }

        var path = new XpsGeometryPath();
        VectorShapes.AddArrow(path, arrow);
        var color = new SolidColorBrush(new Color(arrow.Color.R, arrow.Color.G, arrow.Color.B, (byte)255))
        {
            Opacity = alpha,
        };
        WritePath(path, false, transform, color, null, null, 0, XpsLineCap.Flat, XpsLineJoin.Miter);
    }

    private Color Tinted(Color color) => _tint == Mathematics.Colors.White
        ? color
        : new Color((byte)Math.Round(color.R * _tint.R / 255.0), (byte)Math.Round(color.G * _tint.G / 255.0),
            (byte)Math.Round(color.B * _tint.B / 255.0), color.A);

    internal static XpsMatrix ToXps(Matrix4x4F transform) =>
        new(transform.M11, transform.M12, transform.M21, transform.M22, transform.M41, transform.M42);

    private static bool IsIdentity(Matrix4x4F transform) =>
        transform.M11 == 1 && transform.M12 == 0 && transform.M21 == 0 && transform.M22 == 1 && transform.M41 == 0 &&
        transform.M42 == 0;

    private static bool Invertible(Matrix4x4F transform) =>
        Math.Abs((double)transform.M11 * transform.M22 - (double)transform.M12 * transform.M21) > 1e-12;

    /// <summary>The box of <paramref name="local"/> placed by <paramref name="transform"/>.</summary>
    private static Rect Across(Rect local, Matrix4x4F transform)
    {
        var left = double.PositiveInfinity;
        var top = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        var bottom = double.NegativeInfinity;
        foreach (var corner in (ReadOnlySpan<Vector2<double>>)
                 [new(local.X, local.Y), new(local.Right, local.Y), new(local.X, local.Bottom), new(local.Right, local.Bottom)])
        {
            var x = corner.X * transform.M11 + corner.Y * transform.M21 + transform.M41;
            var y = corner.X * transform.M12 + corner.Y * transform.M22 + transform.M42;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>A path written through to another with every point placed by a matrix.</summary>
    private sealed class TransformedPath(IVectorPath inner, Matrix4x4F transform) : IVectorPath
    {
        public void MoveTo(Vector2<double> point) => inner.MoveTo(Place(point));

        public void LineTo(Vector2<double> point) => inner.LineTo(Place(point));

        public void QuadraticTo(Vector2<double> control, Vector2<double> end) =>
            inner.QuadraticTo(Place(control), Place(end));

        public void CubicTo(Vector2<double> first, Vector2<double> second, Vector2<double> end) =>
            inner.CubicTo(Place(first), Place(second), Place(end));

        public void Close() => inner.Close();

        private Vector2<double> Place(Vector2<double> point) =>
            new(point.X * transform.M11 + point.Y * transform.M21 + transform.M41,
                point.X * transform.M12 + point.Y * transform.M22 + transform.M42);
    }
}
