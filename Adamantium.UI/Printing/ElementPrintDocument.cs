using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Core.Rendering;

namespace Adamantium.UI.Printing;

/// <summary>An element printed as it shows now: each element of it drawn by its own rendering, as on the screen - scrolled
/// from where it is scrolled to, a virtual list with the rows it has made. Laid on the sheet by <see cref="ElementFit"/>,
/// and cut where it runs on over the next sheet. It is printed upright at its laid-out size, whatever turns or scales it
/// on the screen, and whether or not what holds it is showing.
/// <para>What is not vector - a shadow, a glow, a soft edge, a reflection, a live 3D view, a visual brush - is printed
/// as pictures the GPU makes in <see cref="PrepareAsync"/> at the sheet's raster resolution; dispose the document once
/// printed to free them.</para></summary>
public sealed class ElementPrintDocument : IPrintDocument, IDisposable
{
    private readonly IUIComponent _element;
    private readonly ElementFit _fit;
    private readonly IVisualRenderer _renderer;
    private readonly CancellationTokenSource _disposal = new();
    private Rect _area;
    private double _scale = 1;
    private int _columns = 1;
    private int _pages;

    // The pictures drawn now, and the ones being made: one making at a time, for the last layout and scale asked, the one
    // before called off. The pictures drawn now stay until the next are made.
    private ElementRasters _rasters;
    private Task _making;
    private double _makingScale;
    private CancellationTokenSource _makingStop;

    public ElementPrintDocument(IUIComponent element, string title = null, ElementFit fit = ElementFit.Width)
        : this(element, title, fit, null)
    {
    }

    /// <param name="renderer">What makes the pictures; the application's when null.</param>
    internal ElementPrintDocument(IUIComponent element, string title, ElementFit fit, IVisualRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(element);
        _element = element;
        _fit = fit;
        _renderer = renderer;
        Title = title ?? string.Empty;
    }

    public string Title { get; }

    /// <summary>The part of the element printed, in its own units - a page of an editor showing several; the whole
    /// element when null.</summary>
    public Rect? Region { get; init; }

    /// <summary>Makes the pictures of what is not vector at <paramref name="page"/>'s raster resolution, at the scale the
    /// element is laid on the sheet at: once for each layout - the element as <see cref="Paginate"/> found it - and
    /// resolution. Called off, only the wait stops. Without a renderer - no application - everything is printed from its
    /// shapes.</summary>
    public async Task PrepareAsync(PrintPage page, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposal.IsCancellationRequested, this);
        var scale = (page?.RasterDotsPerInch ?? PrintSettings.DefaultRasterDotsPerInch) / 96.0 * _scale;
        while (true)
        {
            // A making that was called off is made again.
            if (_making is null or { IsCanceled: true } || _makingScale != scale)
            {
                StopMaking();
                _makingScale = scale;
                _makingStop = CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token);
                _making = MakeAsync(scale, _makingStop);
            }

            try
            {
                await _making.WaitAsync(cancellation);
                return;
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested &&
                                                     !_disposal.IsCancellationRequested)
            {
                // Called off by another's layout or resolution, not by this caller: made again for this one.
            }
        }
    }

    public void Dispose()
    {
        if (_disposal.IsCancellationRequested)
        {
            return;
        }

        // Called off, not disposed: a making still running reads its token, and a source with no timer holds nothing.
        _disposal.Cancel();
        _rasters?.Dispose();
        _rasters = null;
    }

    // The making is let go of: one still running is called off and frees what it made and its own source as it stops;
    // one done is made again when next asked.
    private void StopMaking()
    {
        _makingStop?.Cancel();
        _makingStop = null;
        _making = null;
    }

    private async Task MakeAsync(double scale, CancellationTokenSource stop)
    {
        try
        {
            if ((_renderer ?? UIApplication.Current?.Container.Resolve<IVisualRenderer>()) is not { } renderer)
            {
                return;
            }

            var rasters = new ElementRasters(scale);
            try
            {
                await rasters.MakeAsync(_element, renderer, stop.Token);
                stop.Token.ThrowIfCancellationRequested();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A picture that cannot be made leaves its element printed from its shapes, as without a GPU - not from
                // pictures of an earlier layout.
                rasters.Dispose();
                _rasters?.Dispose();
                _rasters = null;
                Serilog.Log.Logger.Error(exception, "Making the pictures of {Element} to print failed.",
                    _element.GetType().Name);
                return;
            }
            catch
            {
                rasters.Dispose();
                throw;
            }

            _rasters?.Dispose();
            _rasters = rasters;
        }
        finally
        {
            // Done with: no longer the one to call off.
            if (ReferenceEquals(_makingStop, stop))
            {
                _makingStop = null;
            }

            stop.Dispose();
        }
    }

    /// <summary>Lays the element out on <paramref name="page"/> as it is now; the pictures of it are made again for this
    /// layout by the next <see cref="PrepareAsync"/> - what was made before may show an element since changed.</summary>
    public int Paginate(PrintPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        StopMaking();
        _area = page.ContentArea;

        var own = Region is { } region ? new Size<double>(region.Width, region.Height) : _element.RenderSize;
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
    /// then the next - cut to the sheet's <see cref="PrintPage.ContentArea"/>. The sheets are those of the last
    /// <see cref="Paginate"/>.</summary>
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
        var corner = Region?.TopLeft ?? default;
        var place = Matrix4x4.CreateTranslation((float)-corner.X, (float)-corner.Y, 0f) *
                    Matrix4x4.CreateScale((float)_scale, (float)_scale, 1f) *
                    Matrix4x4.CreateTranslation((float)(_area.X - column * _area.Width),
                        (float)(_area.Y - row * _area.Height), 0f);
        session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(_area) });
        ElementDrawing.Draw(_element, session, place, _area, _rasters);
        session.PopGroup();
    }

    // How many sheets a length runs over; a hair past a whole number is still that number.
    private static int PiecesOf(double length, double piece) =>
        Math.Max(1, (int)Math.Ceiling(length / piece - 1e-9));
}
