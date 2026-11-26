using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;

namespace Adamantium.UI.Core.Printing;

/// <summary>A document laid out on sheets as the print dialog's own options say - which pages, which way up, the scale,
/// how many pages to a sheet. The same sheets are shown in the preview and sent to the printer or the file.</summary>
public sealed class PrintSheets : IPrintDocument
{
    private readonly IPrintDocument _document;
    private readonly PrintSettings _settings;
    private readonly PrinterCapabilities _capabilities;
    private readonly int _current;
    private IReadOnlyList<int> _pages = [];
    private int _columns = 1;
    private int _rows = 1;

    /// <param name="current">The page shown when printing was asked for, from 0 - for
    /// <see cref="PageRange.Current"/>.</param>
    public PrintSheets(IPrintDocument document, PrintSettings settings, PrinterCapabilities capabilities,
        int current = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(capabilities);
        _document = document;
        _settings = settings;
        _capabilities = capabilities;
        _current = current;
        Sheet = new PrintPage(settings.Paper, settings.Orientation, Turned(capabilities.MarginsOf(settings.Paper)));
    }

    public string Title => _document.Title;

    /// <summary>The sheet printed on: the chosen paper, its way up, the printer's margins.</summary>
    public PrintPage Sheet { get; }

    /// <summary>How many pages the document made, all of them - before the range picks.</summary>
    public int DocumentPages { get; private set; }

    /// <summary>The document's pages printed, numbered from 0, in order.</summary>
    public IReadOnlyList<int> Pages => _pages;

    /// <summary>Lays the document out on the sheet - always on <see cref="Sheet"/>, whatever <paramref name="page"/>
    /// says: its options decide the sheet - and returns how many sheets it fills; one at least.</summary>
    public int Paginate(PrintPage page)
    {
        DocumentPages = Math.Max(0, _document.Paginate(Sheet));
        _pages = _settings.Range.Pages(DocumentPages, _current);

        // Several to a sheet are laid out by the first page's size; each is then fitted into its cell by its own.
        var first = _pages.Count > 0 ? OwnSize(_pages[0]) : null;
        var perSheet = Math.Max(1, _settings.PagesPerSheet);
        (_columns, _rows) = Grid(perSheet, PrintableArea, first ?? Sheet.Size, first != null);
        return Math.Max(1, (_pages.Count + perSheet - 1) / perSheet);
    }

    /// <summary>Draws sheet <paramref name="index"/>: each of its pages scaled into its place, cut to it.</summary>
    public void DrawPage(int index, IDrawingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var perSheet = _columns * _rows;
        var area = PrintableArea;
        var cellWidth = area.Width / _columns;
        var cellHeight = area.Height / _rows;
        for (var slot = 0; slot < perSheet; slot++)
        {
            var at = index * perSheet + slot;
            if (at >= _pages.Count)
            {
                break;
            }

            var cell = perSheet == 1
                ? area
                : new Rect(area.X + slot % _columns * cellWidth, area.Y + slot / _columns * cellHeight, cellWidth,
                    cellHeight);
            var place = Place(cell, perSheet == 1, OwnSize(_pages[at]));
            session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(cell) });
            _document.DrawPage(_pages[at], new TransformedDrawingSession(session, place));
            session.PopGroup();
        }
    }

    /// <summary>What the printer reaches of the sheet.</summary>
    private Rect PrintableArea
    {
        get
        {
            var size = Sheet.Size;
            var margins = Sheet.PrintableMargins;
            return new Rect(margins.Left, margins.Top, Math.Max(1, size.Width - margins.Left - margins.Right),
                Math.Max(1, size.Height - margins.Top - margins.Bottom));
        }
    }

    // The page's own size, for a document whose pages keep theirs; null for one laid out on the sheet.
    private Size<double>? OwnSize(int index) =>
        _document.PageSize(index) is { Width: > 0, Height: > 0 } own ? own : null;

    /// <summary>The matrix that puts a page into <paramref name="cell"/>: fitted, at its own size, or by the scale asked
    /// for. A page laid out on the sheet, alone at its own size or a scale of its own, keeps its place on the sheet; a page
    /// of its <paramref name="own"/> size is centred, and turned a quarter - its top to the left - where it fits larger
    /// so, or at a scale asked for fits only so or shows more so.</summary>
    private Matrix4x4F Place(Rect cell, bool alone, Size<double>? own)
    {
        var size = own ?? Sheet.Size;
        var straight = Math.Min(cell.Width / size.Width, cell.Height / size.Height);
        var across = Math.Min(cell.Width / size.Height, cell.Height / size.Width);
        var asked = alone && _settings.Scaling != PrintScaling.FitToSheet;
        var requested = _settings.Scaling == PrintScaling.Custom ? Math.Clamp(_settings.ScalePercent, 10, 400) / 100 : 1;
        // At a scale asked for: turned where only so it fits, or - fitting neither way - where so more of it shows.
        var turned = own != null && (asked
            ? straight < requested && (across >= requested || across > straight)
            : across > straight * (1 + 1e-9));
        var drawn = turned ? new Size<double>(size.Height, size.Width) : size;
        var fit = turned ? across : straight;
        var scale = _settings.Scaling switch
        {
            PrintScaling.ActualSize => alone ? 1 : fit,
            PrintScaling.Custom => (alone ? 1 : fit) * requested,
            _ => fit,
        };
        double x;
        double y;
        if (asked && own == null)
        {
            x = 0;
            y = 0;
        }
        else
        {
            x = cell.X + (cell.Width - drawn.Width * scale) / 2;
            y = cell.Y + (cell.Height - drawn.Height * scale) / 2;
        }

        // Turned, the page's top edge runs up its left: (x, y) goes to (y, width - x).
        Matrix4x4F turn = turned
            ? new Matrix4x4F(0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, (float)size.Width, 0, 1)
            : Matrix4x4F.Identity;
        return turn * Matrix4x4.CreateScale((float)scale, (float)scale, 1f) *
               Matrix4x4.CreateTranslation((float)x, (float)y, 0f);
    }

    /// <summary>The columns and rows <paramref name="count"/> pages are laid in, whichever shows them largest - turned
    /// where they <paramref name="turn"/>, as pages of their own size do.</summary>
    private static (int Columns, int Rows) Grid(int count, Rect area, Size<double> page, bool turn)
    {
        var best = (Columns: count, Rows: 1);
        var largest = 0.0;
        for (var columns = 1; columns <= count; columns++)
        {
            if (count % columns != 0)
            {
                continue;
            }

            var rows = count / columns;
            var scale = Math.Min(area.Width / columns / page.Width, area.Height / rows / page.Height);
            if (turn)
            {
                scale = Math.Max(scale, Math.Min(area.Width / columns / page.Height, area.Height / rows / page.Width));
            }

            if (scale > largest + 1e-12)
            {
                largest = scale;
                best = (columns, rows);
            }
        }

        return best;
    }

    /// <summary>The margins of the paper standing upright, turned with the sheet.</summary>
    private Thickness Turned(Thickness upright) => _settings.Orientation == PageOrientation.Landscape
        ? new Thickness(upright.Bottom, upright.Left, upright.Top, upright.Right)
        : upright;
}
