using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Pdf.Steps;

/// <summary>The end of the group the last <see cref="PdfPushGroupStep"/> began.</summary>
internal sealed class PdfPopGroupStep : PdfDrawStep
{
    public static readonly PdfPopGroupStep Instance = new();

    private PdfPopGroupStep()
    {
    }

    public override void Draw(IDrawingSession session, Matrix4x4F transform) => session.PopGroup();
}
