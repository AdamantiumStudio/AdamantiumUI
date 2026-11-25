using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Graphics.Vector;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.Win32.Printing.Xps;
using static Adamantium.UI.Platforms.Windows.Printing.XpsGeometryPath;

namespace Adamantium.UI.Platforms.Windows.Printing;

// Pictures, tiles and colored meshes. A bitmap goes in as a PNG image - once for the whole job - a drawing's picture as
// the drawing itself, so it stays vector. A tile brush is a visual brush over one cell laid out as the renderer lays it
// (TileBrushLayout), XPS repeating and mirroring it. A mesh is cut into small triangles, each its middle's color.
internal sealed partial class XpsDrawingSession
{
    /// <summary>The most small triangles a mesh triangle is cut into along each side.</summary>
    private const int MaxMeshCuts = 12;

    /// <summary>How long a small triangle's side is at most on the sheet, in DIPs, before it is cut again.</summary>
    private const double MeshCutLength = 3;

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners)
    {
        Picture(image, destinationRect, corners, WholePicture, Matrix4x4F.Identity, -1);
        return this;
    }

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        Rect sourceUv)
    {
        Picture(image, destinationRect, corners, sourceUv, Matrix4x4F.Identity, -1);
        return this;
    }

    public IDrawingSession DrawImage(ImageSource image, Brush filter, Rect destinationRect, Matrix4x4F transform)
    {
        Picture(image, destinationRect, new CornerRadius(0), WholePicture, transform, -1);
        return this;
    }

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer)
    {
        Picture(image, destinationRect, corners, WholePicture, Matrix4x4F.Identity, frameLayer);
        return this;
    }

    public IDrawingSession DrawImageFrame(ImageSource image, Brush filter, Rect destinationRect, CornerRadius corners,
        int frameLayer, Rect sourceUv)
    {
        Picture(image, destinationRect, corners, sourceUv, Matrix4x4F.Identity, frameLayer);
        return this;
    }

    /// <summary>Nothing to write: it readies a picture's texture ahead of drawing it.</summary>
    public IDrawingSession PushImage(ImageSource image) => this;

    public IDrawingSession DrawColoredMesh(ColoredMesh mesh, Matrix4x4F transform)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var vertices = mesh.Vertices;
        var count = (vertices?.Count ?? 0) / 3 * 3;
        if (count == 0 || !Invertible(transform))
        {
            return this;
        }

        var outline = mesh.Outline?.CurvesForDrawing;
        if (outline != null)
        {
            PushGroup(new DrawingGroupOptions { Clip = mesh.Outline, ClipTransform = transform });
        }

        try
        {
            var toPage = transform * Current.ToPage;
            var scale = Math.Sqrt(Math.Abs((double)toPage.M11 * toPage.M22 - (double)toPage.M12 * toPage.M21));
            for (var index = 0; index < count; index += 3)
            {
                Triangle(vertices[index], vertices[index + 1], vertices[index + 2], transform, scale);
            }
        }
        finally
        {
            if (outline != null)
            {
                PopGroup();
            }
        }

        return this;
    }

    /// <summary>One triangle of a mesh, cut into small ones as long as <see cref="MeshCutLength"/> on the sheet, each
    /// filled with the color between the corners at its middle.</summary>
    private void Triangle(ColoredVertex one, ColoredVertex two, ColoredVertex three, Matrix4x4F transform, double scale)
    {
        var a = new Vector2<double>(one.Position.X, one.Position.Y);
        var b = new Vector2<double>(two.Position.X, two.Position.Y);
        var c = new Vector2<double>(three.Position.X, three.Position.Y);
        var longest = Math.Max(VectorShapes.Length(b - a), Math.Max(VectorShapes.Length(c - b), VectorShapes.Length(a - c)));
        var cuts = Math.Clamp((int)Math.Ceiling(longest * scale / MeshCutLength), 1, MaxMeshCuts);
        if (one.Color == two.Color && two.Color == three.Color)
        {
            cuts = 1;
        }

        for (var row = 0; row < cuts; row++)
        {
            for (var column = 0; column < cuts - row; column++)
            {
                // Two small triangles in each step of the grid but the last of a row: one upright, one upside down.
                SmallTriangle(row, column, row + 1, column, row, column + 1);
                if (column < cuts - row - 1)
                {
                    SmallTriangle(row + 1, column, row + 1, column + 1, row, column + 1);
                }
            }
        }

        return;

        void SmallTriangle(int r0, int c0, int r1, int c1, int r2, int c2)
        {
            var p0 = Point(r0, c0);
            var p1 = Point(r1, c1);
            var p2 = Point(r2, c2);
            var middleRow = (r0 + r1 + r2) / 3.0;
            var middleColumn = (c0 + c1 + c2) / 3.0;
            var path = new XpsGeometryPath();
            path.MoveTo(p0);
            path.LineTo(p1);
            path.LineTo(p2);
            path.Close();
            var color = Mix(middleRow / cuts, middleColumn / cuts);
            WritePath(path, false, transform, new SolidColorBrush(color), null, null, 0, XpsLineCap.Flat,
                XpsLineJoin.Miter);
        }

        Vector2<double> Point(int r, int col) => a + (b - a) * ((double)r / cuts) + (c - a) * ((double)col / cuts);

        Color Mix(double towardTwo, double towardThree)
        {
            var share = 1 - towardTwo - towardThree;
            return new Color(
                (byte)Math.Round(one.Color.R * share + two.Color.R * towardTwo + three.Color.R * towardThree),
                (byte)Math.Round(one.Color.G * share + two.Color.G * towardTwo + three.Color.G * towardThree),
                (byte)Math.Round(one.Color.B * share + two.Color.B * towardTwo + three.Color.B * towardThree),
                (byte)Math.Round(one.Color.A * share + two.Color.A * towardTwo + three.Color.A * towardThree));
        }
    }

    /// <summary>Draws <paramref name="image"/> - the part <paramref name="uv"/> of it - into <paramref name="rect"/>,
    /// cut to its corners, where <paramref name="transform"/> puts it; <paramref name="frame"/> is an animation's
    /// frame, -1 for the picture as it is.</summary>
    private void Picture(ImageSource image, Rect rect, CornerRadius corners, Rect uv, Matrix4x4F transform, int frame)
    {
        if (image == null || !(rect.Width > 0) || !(rect.Height > 0) || !(uv.Width > 0) || !(uv.Height > 0) ||
            !Invertible(transform))
        {
            return;
        }

        // The whole picture's rect, of which uv is the part drawn into rect.
        var width = rect.Width / uv.Width;
        var height = rect.Height / uv.Height;
        var whole = new Rect(rect.X - uv.X * width, rect.Y - uv.Y * height, width, height);
        if (image is DrawingImage { Drawing: not null } drawing)
        {
            PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(rect, corners), ClipTransform = transform });
            try
            {
                Placed(transform, () => drawing.Render(this, whole, null));
            }
            finally
            {
                PopGroup();
            }

            return;
        }

        var picture = _pictures.Of(image, frame, new Color(_tint.R, _tint.G, _tint.B, (byte)255), default);
        if (picture == null)
        {
            return;
        }

        var path = new XpsGeometryPath();
        VectorShapes.AddCurves(path, new RectangleGeometry(rect, corners).CurvesForDrawing, true);
        var shape = NewPath(path, false, transform);
        if (shape == null)
        {
            return;
        }

        try
        {
            var brush = ImageBrush(picture, whole);
            try
            {
                Check(shape.SetFillBrushLocal(brush));
            }
            finally
            {
                Marshal.ReleaseComObject(brush);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(shape);
        }
    }

    /// <summary>An image brush painting the whole of <paramref name="picture"/> into <paramref name="whole"/>, once.
    /// </summary>
    private IXpsOMBrush ImageBrush(XpsPictures.Picture picture, Rect whole)
    {
        var viewbox = new XpsRect(0, 0, picture.Width, picture.Height);
        var viewport = new XpsRect((float)whole.X, (float)whole.Y, (float)whole.Width, (float)whole.Height);
        Check(_factory.CreateImageBrush(picture.Resource, viewbox, viewport, out var brush));
        Check(brush.SetTileMode(XpsTileMode.None));
        return (IXpsOMBrush)brush;
    }

    /// <summary>Draws what <paramref name="draw"/> draws in a canvas placed by <paramref name="transform"/> - a drawing's
    /// picture replayed where it is drawn.</summary>
    private void Placed(Matrix4x4F transform, Action draw)
    {
        var canvas = NewCanvas(Current.Visuals);
        try
        {
            if (!IsIdentity(transform))
            {
                Check(_factory.CreateMatrixTransform(ToXps(transform), out var matrix));
                try
                {
                    Check(canvas.SetTransformLocal(matrix));
                }
                finally
                {
                    Marshal.ReleaseComObject(matrix);
                }
            }

            Check(canvas.GetVisuals(out var visuals));
            var level = new Level(visuals, null, transform * Current.ToPage, owned: true);
            _levels.Push(level);
            try
            {
                draw();
            }
            finally
            {
                while (_levels.Peek() != level)
                {
                    PopLevel();
                }

                PopLevel();
            }
        }
        finally
        {
            Marshal.ReleaseComObject(canvas);
        }
    }

    private static bool PaintsTiles(TileBrush brush) =>
        brush is not VisualBrush && brush.Opacity > 0 &&
        brush.ContentSource is BitmapSource { PixelBytes: not null } or DrawingImage { Drawing: not null };

    /// <summary>The tile brush laid over <paramref name="bounds"/> as the renderer lays it - at the scale
    /// <paramref name="transform"/> gives the drawing - as a visual brush over one cell; null when it draws nothing.
    /// </summary>
    private IXpsOMBrush Tiles(TileBrush brush, Rect bounds, Matrix4x4F transform)
    {
        var size = brush.ContentSize;
        if (!(bounds.Width > 0) || !(bounds.Height > 0) || !(size.Width > 0) || !(size.Height > 0))
        {
            return null;
        }

        var toRoot = transform * Current.ToPage;
        var viewport = TileBrushLayout.Viewport(brush, bounds, toRoot.M11, toRoot.M22);
        if (!(viewport.Width > 0) || !(viewport.Height > 0))
        {
            return null;
        }

        var layout = TileBrushLayout.Layout(brush, bounds, toRoot.M11, toRoot.M22);
        var width = viewport.Width;
        var height = viewport.Height;
        var drawn = new Rect(layout.Drawn.X * width, layout.Drawn.Y * height, layout.Drawn.Z * width,
            layout.Drawn.W * height);
        var uv = new Rect(layout.UvRect.X, layout.UvRect.Y, layout.UvRect.Z, layout.UvRect.W);
        if (!(drawn.Width > 0) || !(drawn.Height > 0) || !(uv.Width > 0) || !(uv.Height > 0))
        {
            return null;
        }

        var cell = Cell(brush, drawn, uv, toRoot);
        if (cell == null)
        {
            return null;
        }

        try
        {
            var box = new XpsRect(0, 0, (float)width, (float)height);
            var port = new XpsRect((float)viewport.X, (float)viewport.Y, (float)width, (float)height);
            Check(_factory.CreateVisualBrush(box, port, out var made));
            Check(made.SetVisualLocal((IXpsOMVisual)cell));
            Check(made.SetTileMode(!layout.Repeats ? XpsTileMode.None : brush.TileMode switch
            {
                TileMode.FlipX => XpsTileMode.FlipX,
                TileMode.FlipY => XpsTileMode.FlipY,
                TileMode.FlipXY => XpsTileMode.FlipXY,
                _ => XpsTileMode.Tile,
            }));
            var radians = brush.RotationAngle * Math.PI / 180;
            if (Math.Abs(radians) > 1e-9)
            {
                var centerX = viewport.X + brush.RotationCenter.X * width;
                var centerY = viewport.Y + brush.RotationCenter.Y * height;
                var cos = Math.Cos(radians);
                var sin = Math.Sin(radians);
                Check(_factory.CreateMatrixTransform(new XpsMatrix((float)cos, (float)sin, (float)-sin, (float)cos,
                    (float)(centerX - cos * centerX + sin * centerY), (float)(centerY - sin * centerX - cos * centerY)),
                    out var turn));
                try
                {
                    Check(made.SetTransformLocal(turn));
                }
                finally
                {
                    Marshal.ReleaseComObject(turn);
                }
            }

            if (brush.Opacity < 1 && !_alphaApart)
            {
                Check(made.SetOpacity((float)Math.Clamp(brush.Opacity, 0, 1)));
            }

            return (IXpsOMBrush)made;
        }
        finally
        {
            Marshal.ReleaseComObject(cell);
        }
    }

    /// <summary>One cell of a tile brush: the part <paramref name="uv"/> of its picture in <paramref name="drawn"/>, its
    /// colors changed by the brush's tint, its alpha the tint's. <paramref name="toPage"/> is the scale the cell comes
    /// to on the page - the shape's it fills: a cell is laid at its own size.</summary>
    private IXpsOMCanvas Cell(TileBrush brush, Rect drawn, Rect uv, Matrix4x4F toPage)
    {
        var wholeWidth = drawn.Width / uv.Width;
        var wholeHeight = drawn.Height / uv.Height;
        var whole = new Rect(drawn.X - uv.X * wholeWidth, drawn.Y - uv.Y * wholeHeight, wholeWidth, wholeHeight);
        var tint = Times(_tint, brush.Tint);
        Check(_factory.CreateCanvas(out var cell));
        var clip = new XpsGeometryPath();
        VectorShapes.AddCurves(clip, new RectangleGeometry(drawn).CurvesForDrawing, true);
        SetClip(cell, clip, false, Matrix4x4F.Identity);
        if (tint.A < 255)
        {
            Check(cell.SetOpacity(tint.A / 255f));
        }

        Check(cell.GetVisuals(out var visuals));
        var level = new Level(visuals, null, toPage, owned: true);
        _levels.Push(level);
        var outer = _tint;
        var alphaApart = _alphaApart;
        _tint = new Color(tint.R, tint.G, tint.B, (byte)255);
        _alphaApart = false;
        try
        {
            if (brush.ContentSource is DrawingImage drawing)
            {
                drawing.Render(this, whole, null);
                return cell;
            }

            var picture = _pictures.Of(brush.ContentSource, -1, _tint, brush.ColorAdjustment);
            if (picture == null)
            {
                Marshal.ReleaseComObject(cell);
                return null;
            }

            var path = new XpsGeometryPath();
            VectorShapes.AddCurves(path, new RectangleGeometry(drawn).CurvesForDrawing, true);
            var shape = NewPath(path, false, Matrix4x4F.Identity);
            try
            {
                var fill = ImageBrush(picture, whole);
                try
                {
                    Check(shape.SetFillBrushLocal(fill));
                }
                finally
                {
                    Marshal.ReleaseComObject(fill);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(shape);
            }

            return cell;
        }
        finally
        {
            _tint = outer;
            _alphaApart = alphaApart;
            while (_levels.Peek() != level)
            {
                PopLevel();
            }

            PopLevel();
        }
    }

    private static Color Times(Color one, Color other) => new((byte)Math.Round(one.R * other.R / 255.0),
        (byte)Math.Round(one.G * other.G / 255.0), (byte)Math.Round(one.B * other.B / 255.0),
        (byte)Math.Round(one.A * other.A / 255.0));
}
