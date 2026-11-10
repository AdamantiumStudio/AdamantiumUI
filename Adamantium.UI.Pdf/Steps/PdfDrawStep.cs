using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>One draw of a PDF page as it was read, in the page's units: replayed into a session with the matrix that
/// places the page.</summary>
internal abstract class PdfDrawStep
{
    public abstract void Draw(IDrawingSession session, Matrix4x4F transform);
}
