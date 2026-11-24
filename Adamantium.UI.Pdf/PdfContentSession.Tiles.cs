using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Writing;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf;

// Tile brushes: a picture or a drawing laid out as the renderer lays it (TileBrushLayout) - viewport, viewbox, stretch
// and alignment, turned about its center - one copy, or repeated as a tiling pattern, mirrored for the flip modes. A
// bitmap goes in as an image, a drawing as itself, in vector.
public sealed partial class PdfContentSession
{
    /// <summary>How far apart the copies of a tile that does not repeat are put, in cells, when it is a pattern - a
    /// stroke's - so only the one is ever seen.</summary>
    private const double LoneTileStep = 1e5;

    /// <summary>A tile brush made ready to paint with: its cell's content, the cell's box and step, and the matrix from
    /// the cell's space to the shape's units.</summary>
    private sealed class TilePaint(PdfContentBuilder cell, PdfRectangle box, PdfMatrix toShape, bool repeats)
    {
        public PdfMatrix ToShape { get; } = toShape;

        public bool Repeats { get; } = repeats;

        /// <summary>The tiles as a pattern, <paramref name="place"/> taking the shape's units to the content's.</summary>
        public PdfStream Pattern(PdfMatrix place) => cell.ToTilingPattern(box,
            Repeats ? box.Width : box.Width * LoneTileStep, Repeats ? box.Height : box.Height * LoneTileStep,
            ToShape.Then(place));

        /// <summary>The one copy, as a form in the cell's space.</summary>
        public PdfStream Once() => cell.ToFormXObject(box);
    }

    private static bool PaintsTiles(TileBrush brush) =>
        brush is not VisualBrush && brush.Opacity > 0 &&
        brush.ContentSource is BitmapSource { PixelBytes: not null } or DrawingImage { Drawing: not null };

    /// <summary>The tile brush laid over <paramref name="bounds"/>; null when it draws nothing or its picture has no
    /// pixels to give.</summary>
    private TilePaint Tiles(TileBrush brush, Rect bounds)
    {
        var size = brush.ContentSize;
        if (!(bounds.Width > 0) || !(bounds.Height > 0) || !(size.Width > 0) || !(size.Height > 0))
        {
            return null;
        }

        var viewport = TileBrushLayout.Viewport(brush, bounds, 1, 1);
        if (!(viewport.Width > 0) || !(viewport.Height > 0))
        {
            return null;
        }

        var layout = TileBrushLayout.Layout(brush, bounds, 1, 1);
        var width = viewport.Width;
        var height = viewport.Height;
        var flipX = brush.TileMode is TileMode.FlipX or TileMode.FlipXY;
        var flipY = brush.TileMode is TileMode.FlipY or TileMode.FlipXY;
        var columns = flipX ? 2 : 1;
        var rows = flipY ? 2 : 1;
        var drawn = new Rect(layout.Drawn.X * width, layout.Drawn.Y * height, layout.Drawn.Z * width,
            layout.Drawn.W * height);
        var uv = new Rect(layout.UvRect.X, layout.UvRect.Y, layout.UvRect.Z, layout.UvRect.W);
        if (!(drawn.Width > 0) || !(drawn.Height > 0) || !(uv.Width > 0) || !(uv.Height > 0))
        {
            return null;
        }

        var cell = new PdfContentBuilder();
        for (var column = 0; column < columns; column++)
        {
            for (var row = 0; row < rows; row++)
            {
                // The second copy along a flipped axis is the first mirrored into its own half of the cell.
                var copy = new PdfMatrix(column == 1 ? -1 : 1, 0, 0, row == 1 ? -1 : 1,
                    column == 1 ? 2 * width : 0, row == 1 ? 2 * height : 0);
                if (!TileCopy(cell, brush, drawn, uv, copy))
                {
                    return null;
                }
            }
        }

        var toShape = new PdfMatrix(1, 0, 0, 1, viewport.X, viewport.Y);
        var radians = brush.RotationAngle * Math.PI / 180;
        if (Math.Abs(radians) > 1e-9)
        {
            var centerX = viewport.X + brush.RotationCenter.X * width;
            var centerY = viewport.Y + brush.RotationCenter.Y * height;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            toShape = toShape.Then(new PdfMatrix(1, 0, 0, 1, -centerX, -centerY))
                .Then(new PdfMatrix(cos, sin, -sin, cos, 0, 0))
                .Then(new PdfMatrix(1, 0, 0, 1, centerX, centerY));
        }

        return new TilePaint(cell, new PdfRectangle(0, 0, columns * width, rows * height), toShape, layout.Repeats);
    }

    /// <summary>One copy of the tile's picture into <paramref name="cell"/>: the part <paramref name="uv"/> of it in
    /// <paramref name="drawn"/>, placed by <paramref name="copy"/>, its colors changed by the brush's tint.</summary>
    private bool TileCopy(PdfContentBuilder cell, TileBrush brush, Rect drawn, Rect uv, PdfMatrix copy)
    {
        var source = brush.ContentSource;
        // The whole picture's rect, of which uv is the part drawn.
        var wholeWidth = drawn.Width / uv.Width;
        var wholeHeight = drawn.Height / uv.Height;
        var whole = new Rect(drawn.X - uv.X * wholeWidth, drawn.Y - uv.Y * wholeHeight, wholeWidth, wholeHeight);
        var box = new PdfRectangle(drawn.X, drawn.Y, drawn.Right, drawn.Bottom);
        PdfStream piece;
        if (source is DrawingImage drawing)
        {
            var content = Capture(() => drawing.Render(this, whole, null));
            piece = Tinted(content.ToFormXObject(box), box, brush.Tint);
        }
        else
        {
            var picture = _pictures.Of(source, -1, brush.Tint, brush.ColorAdjustment);
            if (picture == null)
            {
                return false;
            }

            var image = new PdfContentBuilder().Transform(whole.Width, 0, 0, -whole.Height, whole.X,
                whole.Y + whole.Height).Draw(picture);
            piece = image.ToFormXObject(box);
        }

        cell.SaveState().Transform(copy.A, copy.B, copy.C, copy.D, copy.E, copy.F).Draw(piece).RestoreState();
        return true;
    }

    /// <summary>A drawing's piece with <paramref name="tint"/> multiplied in: the tint laid over it by multiply, in a
    /// group let through only where the drawing itself is - by its alpha - and faded by the tint's alpha.</summary>
    private static PdfStream Tinted(PdfStream piece, PdfRectangle box, Color tint)
    {
        if (tint == Mathematics.Colors.White)
        {
            return piece;
        }

        var alpha = new PdfContentBuilder().Draw(piece).ToFormXObject(box, PdfTransparencyGroup.Layer);
        var multiplied = new PdfContentBuilder().Draw(piece).BlendMode(PdfBlendMode.Multiply)
            .FillColor(tint.R / 255.0, tint.G / 255.0, tint.B / 255.0)
            .Rectangle(box.Left, box.Bottom, box.Width, box.Height).Fill()
            .ToFormXObject(box, PdfTransparencyGroup.Layer);
        var tinted = new PdfContentBuilder().SaveState().SoftMask(alpha, false);
        if (tint.A < 255)
        {
            tinted.Opacity(tint.A / 255.0, tint.A / 255.0);
        }

        return tinted.Draw(multiplied).RestoreState().ToFormXObject(box);
    }
}
