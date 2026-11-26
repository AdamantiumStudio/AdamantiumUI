using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;

namespace Adamantium.UI.Printing;

/// <summary>An element printed as it shows now: each element of it drawn by its own rendering, as on the screen - scrolled
/// from where it is scrolled to, a virtual list with the rows it has made. Laid on the sheet by <see cref="ElementFit"/>,
/// and cut where it runs on over the next sheet. It is printed upright at its laid-out size, whatever turns or scales it
/// on the screen, and whether or not what holds it is showing.</summary>
public sealed class ElementPrintDocument : IPrintDocument
{
    private readonly IUIComponent _element;
    private readonly ElementFit _fit;
    private Rect _area;
    private double _scale = 1;
    private int _columns = 1;
    private int _pages;

    public ElementPrintDocument(IUIComponent element, string title = null, ElementFit fit = ElementFit.Width)
    {
        ArgumentNullException.ThrowIfNull(element);
        _element = element;
        _fit = fit;
        Title = title ?? string.Empty;
    }

    public string Title { get; }

    public int Paginate(PrintPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var size = page.Size;
        var margins = page.PrintableMargins;
        _area = new Rect(margins.Left, margins.Top, Math.Max(1, size.Width - margins.Left - margins.Right),
            Math.Max(1, size.Height - margins.Top - margins.Bottom));

        var own = _element.RenderSize;
        if (!(own.Width > 0) || !(own.Height > 0))
        {
            _scale = 1;
            _columns = 1;
            return _pages = 1;
        }

        _scale = _fit == ElementFit.Width ? Math.Min(1, _area.Width / own.Width) : 1;
        _columns = _fit == ElementFit.Width ? 1 : PiecesOf(own.Width * _scale, _area.Width);
        return _pages = _columns * PiecesOf(own.Height * _scale, _area.Height);
    }

    /// <summary>Draws the part of the element sheet <paramref name="index"/> holds - counted along the first row of sheets,
    /// then the next - cut to what the printer reaches. The sheets are those of the last <see cref="Paginate"/>.</summary>
    public void DrawPage(int index, IDrawingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_pages == 0)
        {
            throw new InvalidOperationException("Paginate lays the element out on a sheet before a page is drawn.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _pages);
        var column = index % _columns;
        var row = index / _columns;
        var place = Matrix4x4.CreateScale((float)_scale, (float)_scale, 1f) *
                    Matrix4x4.CreateTranslation((float)(_area.X - column * _area.Width),
                        (float)(_area.Y - row * _area.Height), 0f);
        session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(_area) });
        ElementDrawing.Draw(_element, session, place, _area);
        session.PopGroup();
    }

    // How many sheets a length runs over; a hair past a whole number is still that number.
    private static int PiecesOf(double length, double piece) =>
        Math.Max(1, (int)Math.Ceiling(length / piece - 1e-9));
}
