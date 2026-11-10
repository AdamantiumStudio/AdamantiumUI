using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media.Imaging;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>An image, its unit square placed by the matrix with its first row on top.</summary>
internal sealed class PdfImageStep(ImageSource image, Matrix4x4F matrix) : PdfDrawStep
{
    private static readonly Rect Unit = new(0, 0, 1, 1);

    public ImageSource Image { get; } = image;

    public Matrix4x4F Matrix { get; } = matrix;

    public override void Draw(IDrawingSession session, Matrix4x4F transform) =>
        session.DrawImage(Image, null, Unit, Matrix * transform);
}
