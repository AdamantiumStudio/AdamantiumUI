using System;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Printing;

/// <summary>One sheet as it will be printed: the paper, fitted into the control's room, and what is printed on it drawn
/// by the same layout that goes to the printer or the file.</summary>
public class PrintPreview : MeasurableUIComponent
{
    /// <summary>The room left round the paper, in DIPs.</summary>
    private const double Room = 12;

    private static readonly double[] MarginsDashes = [4, 3];

    // The sheets being made ready, called off when others are shown or the preview goes; the sheet shown, made ready
    // once they are, called off when another is shown.
    private CancellationTokenSource _preparing;
    private CancellationTokenSource _preparingSheet;
    private Task _ready;
    private PrintPage _screen;

    public static readonly AdamantiumProperty SheetsProperty = AdamantiumProperty.Register(nameof(Sheets),
        typeof(PrintSheets), typeof(PrintPreview),
        new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender, OnSheetsChanged));

    public static readonly AdamantiumProperty SheetProperty = AdamantiumProperty.Register(nameof(Sheet),
        typeof(int), typeof(PrintPreview), new PropertyMetadata(0, PropertyMetadataOptions.AffectsRender, OnSheetChanged));

    public static readonly AdamantiumProperty ShowsMarginsProperty = AdamantiumProperty.Register(nameof(ShowsMargins),
        typeof(bool), typeof(PrintPreview), new PropertyMetadata(false, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty MarginsBrushProperty = AdamantiumProperty.Register(nameof(MarginsBrush),
        typeof(Brush), typeof(PrintPreview), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty PaperBrushProperty = AdamantiumProperty.Register(nameof(PaperBrush),
        typeof(Brush), typeof(PrintPreview), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    public static readonly AdamantiumProperty ShadowBrushProperty = AdamantiumProperty.Register(nameof(ShadowBrush),
        typeof(Brush), typeof(PrintPreview), new PropertyMetadata(null, PropertyMetadataOptions.AffectsRender));

    /// <summary>The sheets shown - laid out already.</summary>
    public PrintSheets Sheets
    {
        get => GetValue<PrintSheets>(SheetsProperty);
        set => SetValue(SheetsProperty, value);
    }

    /// <summary>Which sheet, from 0.</summary>
    public int Sheet
    {
        get => GetValue<int>(SheetProperty);
        set => SetValue(SheetProperty, value);
    }

    /// <summary>What the paper is drawn in - white, as paper is, in any theme.</summary>
    public Brush PaperBrush
    {
        get => GetValue<Brush>(PaperBrushProperty);
        set => SetValue(PaperBrushProperty, value);
    }

    /// <summary>What the paper's edge and shadow are drawn in.</summary>
    public Brush ShadowBrush
    {
        get => GetValue<Brush>(ShadowBrushProperty);
        set => SetValue(ShadowBrushProperty, value);
    }

    /// <summary>Whether the paper's margins are drawn round what fills the sheet, dashed - while they are being set.
    /// </summary>
    public bool ShowsMargins
    {
        get => GetValue<bool>(ShowsMarginsProperty);
        set => SetValue(ShowsMarginsProperty, value);
    }

    /// <summary>What the margins are drawn in.</summary>
    public Brush MarginsBrush
    {
        get => GetValue<Brush>(MarginsBrushProperty);
        set => SetValue(MarginsBrushProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PrintPreviewAutomationPeer(this);

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopPreparing();
    }

    // Back in a tree - a template applied again - the sheets it shows are made ready again; the last wait was called off.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Prepare();
    }

    private static void OnSheetsChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e) =>
        ((PrintPreview)d).Prepare();

    private static void OnSheetChanged(AdamantiumComponent d, AdamantiumPropertyChangedEventArgs e) =>
        ((PrintPreview)d).PrepareSheet();

    // New sheets are shown as soon as they are laid out and drawn again once they are ready: pictures of what is not
    // vector come from the GPU, and until they do, those parts are drawn from their shapes; the pages of the sheet shown
    // are made ready next, and again for each sheet turned to. They are made as fine as the screen shows a sheet at its
    // own size - the preview shows it smaller - not as fine as the printer prints.
    private void Prepare()
    {
        StopPreparing();
        if (Sheets is not { } sheets || !IsAttachedToVisualTree)
        {
            return;
        }

        var sheet = sheets.Sheet;
        _screen = new PrintPage(sheet.Paper, sheet.Orientation, sheet.PrintableMargins)
        {
            RasterDotsPerInch = Math.Max(1, (int)Math.Round(96 * (RootVisual?.Window?.DpiScale.X ?? 1))),
        };
        _preparing = new CancellationTokenSource();
        _ready = MakeReadyAsync(sheets, _screen, _preparing.Token);
        PrepareSheet();
    }

    private void PrepareSheet()
    {
        StopPreparingSheet();
        if (Sheets is not { } sheets || _preparing == null)
        {
            return;
        }

        _preparingSheet = CancellationTokenSource.CreateLinkedTokenSource(_preparing.Token);
        _ = PrepareSheetAsync(sheets, Math.Max(0, Sheet), _screen, _ready, _preparingSheet.Token);
    }

    private static async Task MakeReadyAsync(PrintSheets sheets, PrintPage screen, CancellationToken cancellation)
    {
        try
        {
            await sheets.PrepareAsync(screen, cancellation);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Error(exception, "Making the print preview's sheets ready failed.");
        }
    }

    private async Task PrepareSheetAsync(PrintSheets sheets, int sheet, PrintPage screen, Task ready,
        CancellationToken cancellation)
    {
        await ready;
        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await sheets.PreparePagesAsync([sheet], screen, cancellation);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Error(exception, "Making the print preview's sheet ready failed.");
        }

        if (ReferenceEquals(Sheets, sheets))
        {
            InvalidateRender(false);
        }
    }

    private void StopPreparing()
    {
        StopPreparingSheet();
        _preparing?.Cancel();
        _preparing?.Dispose();
        _preparing = null;
        _ready = null;
    }

    private void StopPreparingSheet()
    {
        _preparingSheet?.Cancel();
        _preparingSheet?.Dispose();
        _preparingSheet = null;
    }

    protected override void OnRender(IDrawingContext context)
    {
        base.OnRender(context);
        Draw(context.ForControl(this));
    }

    /// <summary>Where the paper is drawn in the control, fitted; empty when there is nothing to show.</summary>
    internal Rect Paper
    {
        get
        {
            var size = RenderSize;
            if (Sheets is not { } sheets || size.Width <= Room * 2 || size.Height <= Room * 2)
            {
                return default;
            }

            var sheet = sheets.Sheet.Size;
            var scale = Math.Min((size.Width - Room * 2) / sheet.Width, (size.Height - Room * 2) / sheet.Height);
            var width = sheet.Width * scale;
            var height = sheet.Height * scale;
            return new Rect((size.Width - width) / 2, (size.Height - height) / 2, width, height);
        }
    }

    /// <summary>Draws the sheet into a session: the paper, its shadow, and the sheet's pages cut to it.</summary>
    internal void Draw(IDrawingSession session)
    {
        var paper = Paper;
        if (session == null || Sheets is not { } sheets || paper.Width <= 0 || paper.Height <= 0)
        {
            return;
        }

        if (ShadowBrush is { } shadow)
        {
            session.DrawRectangle(shadow, new Rect(paper.X + 2, paper.Y + 3, paper.Width, paper.Height));
        }

        session.DrawRectangle(PaperBrush ?? new SolidColorBrush(Colors.White), paper);
        var scale = paper.Width / sheets.Sheet.Size.Width;
        session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(paper) });
        sheets.DrawPage(Math.Max(0, Sheet), new TransformedDrawingSession(session,
            Matrix4x4.CreateScale((float)scale, (float)scale, 1f) *
            Matrix4x4.CreateTranslation((float)paper.X, (float)paper.Y, 0f)));
        session.PopGroup();

        if (ShowsMargins && MarginsBrush is { } margins)
        {
            var area = sheets.Sheet.ContentArea;
            session.DrawRectangle(null, new Rect(paper.X + area.X * scale, paper.Y + area.Y * scale,
                area.Width * scale, area.Height * scale), new Pen(margins, 1, dashStrokeArray: MarginsDashes));
        }
    }
}
