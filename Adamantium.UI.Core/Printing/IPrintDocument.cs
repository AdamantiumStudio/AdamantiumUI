using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;

namespace Adamantium.UI.Core.Printing;

/// <summary>What is printed: pages laid out on a sheet and drawn as they are on screen. A text document lays itself out
/// anew for each sheet it is given; a PDF keeps its own pages.</summary>
public interface IPrintDocument
{
    /// <summary>The name a print job and a saved file take.</summary>
    string Title { get; }

    /// <summary>Lays the document out on <paramref name="page"/>, and returns how many pages it makes. Called again
    /// whenever the sheet changes; <see cref="DrawPage"/> draws the pages of the last call.</summary>
    int Paginate(PrintPage page);

    /// <summary>Draws page <paramref name="index"/> into <paramref name="session"/>, in DIPs from the top left corner of
    /// the sheet. What it draws inside <see cref="PrintPage.PrintableMargins"/> may not be printed.</summary>
    void DrawPage(int index, IDrawingSession session);

    /// <summary>The size page <paramref name="index"/> is drawn at, in DIPs, for a document whose pages keep their own
    /// sizes - a PDF's; null for one laid out on the sheet, drawn at its size. The sheet's scale, its place and a turn
    /// to fit it are worked out from this.</summary>
    Size<double>? PageSize(int index) => null;

    /// <summary>Makes ready, after <see cref="Paginate"/> and before the first <see cref="DrawPage"/>, what drawing the
    /// pages takes and cannot be made while drawing - pictures of what is not vector, made by the GPU at
    /// <see cref="PrintPage.RasterDotsPerInch"/>. Called on the thread that draws; nothing to make ready by default.
    /// </summary>
    Task PrepareAsync(PrintPage page, CancellationToken cancellation) => Task.CompletedTask;
}
