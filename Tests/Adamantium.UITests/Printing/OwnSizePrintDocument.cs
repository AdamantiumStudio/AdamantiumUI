using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UITests.Printing;

/// <summary>Pages of their own sizes - a PDF's - each drawing one rectangle of its whole size.</summary>
internal sealed class OwnSizePrintDocument(params Size<double>[] sizes) : IPrintDocument
{
    public string Title => "Own sizes";

    public int Paginate(PrintPage page) => sizes.Length;

    public void DrawPage(int index, IDrawingSession session) =>
        session.DrawGeometry(new SolidColorBrush(new Color((byte)index, (byte)0, (byte)0, (byte)255)),
            new RectangleGeometry(new Rect(0, 0, sizes[index].Width, sizes[index].Height)), null);

    public Size<double>? PageSize(int index) => sizes[index];

    public bool HasOwnMargins => true;
}
