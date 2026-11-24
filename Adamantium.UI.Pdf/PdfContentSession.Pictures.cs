using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Graphics;
using Adamantium.Pdf.Writing;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf;

// Pictures and colored meshes. A bitmap goes in as an image - once for every page that shows it - a drawing's picture as
// the drawing itself, so it stays vector; a mesh as triangles shaded between their corners.
public sealed partial class PdfContentSession
{
    private static readonly Rect WholePicture = new(0, 0, 1, 1);

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
        var place = Through(transform);
        if (count == 0 || !Invertible(place))
        {
            return this;
        }

        var colors = new List<PdfShadedVertex>(count);
        var alphas = new List<PdfShadedVertex>(count);
        var opaque = true;
        for (var index = 0; index < count; index++)
        {
            var vertex = vertices[index];
            var alpha = vertex.Color.A / 255.0;
            opaque &= vertex.Color.A == 255;
            var color = Tinted(vertex.Color);
            colors.Add(new PdfShadedVertex(vertex.Position.X, vertex.Position.Y, color.R / 255.0, color.G / 255.0,
                color.B / 255.0));
            alphas.Add(new PdfShadedVertex(vertex.Position.X, vertex.Position.Y, alpha, alpha, alpha));
        }

        var content = Content;
        var box = Box(mesh.Bounds, place, 0);
        content.SaveState();
        if (!opaque)
        {
            var mask = new PdfContentBuilder();
            mask.Transform(place.A, place.B, place.C, place.D, place.E, place.F).Shade(PdfShadings.Triangles(alphas));
            content.SoftMask(mask.ToFormXObject(box, PdfTransparencyGroup.Layer), true);
        }

        Transform(place);
        if (mesh.Outline?.CurvesForDrawing is { } outline && Write(outline, true, PdfMatrix.Identity))
        {
            content.Clip(outline.FillRule == FillRule.EvenOdd);
        }

        content.Shade(PdfShadings.Triangles(colors));
        content.RestoreState();
        _levels.Peek().Grow(box);
        return this;
    }

    /// <summary>Draws <paramref name="image"/> - the part <paramref name="uv"/> of it - into <paramref name="rect"/>,
    /// cut to its corners, where <paramref name="transform"/> puts it; <paramref name="frame"/> is an animation's
    /// frame, -1 for the picture as it is.</summary>
    private void Picture(ImageSource image, Rect rect, CornerRadius corners, Rect uv, Matrix4x4F transform, int frame)
    {
        var place = Through(transform);
        if (image == null || !(rect.Width > 0) || !(rect.Height > 0) || !(uv.Width > 0) || !(uv.Height > 0) ||
            !Invertible(place))
        {
            return;
        }

        // The whole picture's rect, of which uv is the part drawn into rect.
        var width = rect.Width / uv.Width;
        var height = rect.Height / uv.Height;
        var whole = new Rect(rect.X - uv.X * width, rect.Y - uv.Y * height, width, height);
        var cut = uv != WholePicture || !Square(corners);

        if (image is DrawingImage { Drawing: not null } drawing)
        {
            PushGroup(new DrawingGroupOptions
            {
                Clip = new RectangleGeometry(rect, corners),
                ClipTransform = transform,
            });
            PushInner(transform);
            try
            {
                drawing.Render(this, whole, null);
            }
            finally
            {
                PopInner();
                PopGroup();
            }

            return;
        }

        var picture = _pictures.Of(image, frame, new Color(_tint.R, _tint.G, _tint.B, (byte)255), default);
        if (picture == null)
        {
            return;
        }

        var content = Content;
        content.SaveState();
        if (cut && Write(new RectangleGeometry(rect, corners).CurvesForDrawing, true, place))
        {
            content.Clip();
        }

        // The image fills its unit square, first row at the top: (0, 1) is the rect's top left corner.
        var toPage = new PdfMatrix(whole.Width, 0, 0, -whole.Height, whole.X, whole.Y + whole.Height).Then(place);
        Transform(toPage);
        content.Draw(picture);
        content.RestoreState();
        GrowBox(rect, place, 0);
    }

    private static bool Square(CornerRadius corners) =>
        corners.TopLeft <= 0 && corners.TopRight <= 0 && corners.BottomRight <= 0 && corners.BottomLeft <= 0;
}
