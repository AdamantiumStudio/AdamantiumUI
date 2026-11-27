using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UITests.Printing;

/// <summary>Pages that draw nothing, and tell which were made ready - each call's pages, in order - and on which sheet
/// they were laid out.</summary>
internal sealed class PreparedPagesDocument(int count, bool hasOwnMargins = false) : IPrintDocument
{
    public List<IReadOnlyList<int>> Prepared { get; } = [];

    public PrintPage LaidOutOn { get; private set; }

    public string Title => "Prepared pages";

    public bool HasOwnMargins => hasOwnMargins;

    public int Paginate(PrintPage page)
    {
        LaidOutOn = page;
        return count;
    }

    public Task PreparePagesAsync(IReadOnlyList<int> pages, PrintPage page, CancellationToken cancellation)
    {
        Prepared.Add([.. pages]);
        return Task.CompletedTask;
    }

    public void DrawPage(int index, IDrawingSession session)
    {
    }
}
