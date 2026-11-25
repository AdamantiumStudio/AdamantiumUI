using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.Collections;
using Adamantium.UI.Controls.Automation;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Core.RoutedEvents;

namespace Adamantium.UI.Controls.Printing;

/// <summary>The print dialog: a preview of the sheets, and the options - the platform's own (which pages, which way up,
/// the scale, pages to a sheet), which every printer and the PDF file have, and the printer's, each shown only when the
/// printer chosen offers more than one choice of it, offering only those. Choices that cannot go together are put
/// right by the system, and the dialog shows what it made of them.
/// <para>It holds no printer itself: <see cref="Platform"/> - the system's - answers which printers there are and what
/// they can do; without one, only the PDF file is offered. <see cref="Print"/> and <see cref="Canceled"/> say how it
/// was left; who opened it does the printing.</para></summary>
public class PrintDialog : Control
{
    private PrinterCapabilities _capabilities = PrinterCapabilities.Pdf;
    private CancellationTokenSource _asking;
    private bool _composing;

    // What the user asked for, before any printer fitted it: a choice one printer cannot make comes back with the
    // next that can.
    private PrintSettings _wish = new() { Scaling = PrintScaling.FitToSheet, Color = PrintColorMode.Color };

    // What the sheets were last laid out from: only a choice that moves pages lays them out again.
    private PrintSettings _laidOut;
    private PrinterCapabilities _laidOutFor;
    private IPrintDocument _laidOutDocument;
    private int _laidOutCurrentPage;

    public PrintDialog()
    {
        PrintCommand = new PrintDialogCommand(OnPrint, () => RangeIsValid && !IsAsking);
        CancelCommand = new PrintDialogCommand(OnCancel);
        PreviousSheetCommand = new PrintDialogCommand(() => Sheet--, () => Sheet > 0);
        NextSheetCommand = new PrintDialogCommand(() => Sheet++, () => Sheet < SheetCount - 1);
        SetCurrentValue(PrinterProperty, Printer.Pdf);
        Compose();
    }

    #region What is printed, and by whom

    public static readonly AdamantiumProperty DocumentProperty = AdamantiumProperty.Register(nameof(Document),
        typeof(IPrintDocument), typeof(PrintDialog), new PropertyMetadata(null, OnOptionChanged));

    public static readonly AdamantiumProperty PlatformProperty = AdamantiumProperty.Register(nameof(Platform),
        typeof(IPrintPlatform), typeof(PrintDialog), new PropertyMetadata(null, OnPlatformChanged));

    public static readonly AdamantiumProperty CurrentPageProperty = AdamantiumProperty.Register(nameof(CurrentPage),
        typeof(int), typeof(PrintDialog), new PropertyMetadata(0, OnOptionChanged));

    public static readonly AdamantiumProperty PrinterProperty = AdamantiumProperty.Register(nameof(Printer),
        typeof(Printer), typeof(PrintDialog), new PropertyMetadata(null, OnPrinterChanged));

    /// <summary>What is printed.</summary>
    public IPrintDocument Document
    {
        get => GetValue<IPrintDocument>(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    /// <summary>The system's printers; null offers the PDF file alone.</summary>
    public IPrintPlatform Platform
    {
        get => GetValue<IPrintPlatform>(PlatformProperty);
        set => SetValue(PlatformProperty, value);
    }

    /// <summary>The page shown when printing was asked for, from 0 - what "Current page" prints.</summary>
    public int CurrentPage
    {
        get => GetValue<int>(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    /// <summary>The PDF file, then the system's printers - one list, filled in as the system answers, so the printer
    /// chosen in it stays among its items.</summary>
    public TrackingCollection<Printer> Printers { get; } = [Printer.Pdf];

    /// <summary>Where it is printed.</summary>
    public Printer Printer
    {
        get => GetValue<Printer>(PrinterProperty);
        set => SetValue(PrinterProperty, value);
    }

    #endregion

    #region The platform's own options

    public static readonly AdamantiumProperty RangeKindProperty = AdamantiumProperty.Register(nameof(RangeKind),
        typeof(PageRangeKind), typeof(PrintDialog), new PropertyMetadata(PageRangeKind.All, OnOptionChanged));

    public static readonly AdamantiumProperty RangeTextProperty = AdamantiumProperty.Register(nameof(RangeText),
        typeof(string), typeof(PrintDialog), new PropertyMetadata(string.Empty, OnRangeTextChanged));

    public static readonly AdamantiumProperty RangeIsValidProperty = AdamantiumProperty.Register(nameof(RangeIsValid),
        typeof(bool), typeof(PrintDialog), new PropertyMetadata(true));

    public static readonly AdamantiumProperty OrientationProperty = AdamantiumProperty.Register(nameof(Orientation),
        typeof(PageOrientation), typeof(PrintDialog), new PropertyMetadata(PageOrientation.Portrait, OnOptionChanged));

    public static readonly AdamantiumProperty PaperProperty = AdamantiumProperty.Register(nameof(Paper),
        typeof(PaperSize), typeof(PrintDialog), new PropertyMetadata(PaperSize.A4, OnOptionChanged));

    public static readonly AdamantiumProperty PapersProperty = AdamantiumProperty.Register(nameof(Papers),
        typeof(IReadOnlyList<PaperSize>), typeof(PrintDialog), new PropertyMetadata(PaperSize.Standard));

    public static readonly AdamantiumProperty ScalingProperty = AdamantiumProperty.Register(nameof(Scaling),
        typeof(PrintScaling), typeof(PrintDialog), new PropertyMetadata(PrintScaling.FitToSheet, OnOptionChanged));

    public static readonly AdamantiumProperty ScalePercentProperty = AdamantiumProperty.Register(nameof(ScalePercent),
        typeof(double), typeof(PrintDialog), new PropertyMetadata(100.0, OnOptionChanged));

    public static readonly AdamantiumProperty PagesPerSheetProperty = AdamantiumProperty.Register(
        nameof(PagesPerSheet), typeof(int), typeof(PrintDialog), new PropertyMetadata(1, OnOptionChanged));

    /// <summary>All pages, the current one, or those in <see cref="RangeText"/>.</summary>
    public PageRangeKind RangeKind
    {
        get => GetValue<PageRangeKind>(RangeKindProperty);
        set => SetValue(RangeKindProperty, value);
    }

    /// <summary>The pages written out - "1-3, 5" - for <see cref="PageRangeKind.Chosen"/>; typing it chooses it.</summary>
    public string RangeText
    {
        get => GetValue<string>(RangeTextProperty);
        set => SetValue(RangeTextProperty, value);
    }

    /// <summary>Whether <see cref="RangeText"/> writes out pages when it is chosen; printing waits until it does.</summary>
    public bool RangeIsValid => GetValue<bool>(RangeIsValidProperty);

    public PageOrientation Orientation
    {
        get => GetValue<PageOrientation>(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public PaperSize Paper
    {
        get => GetValue<PaperSize>(PaperProperty);
        set => SetValue(PaperProperty, value);
    }

    /// <summary>The paper the printer takes - every standard size for the PDF file.</summary>
    public IReadOnlyList<PaperSize> Papers => GetValue<IReadOnlyList<PaperSize>>(PapersProperty);

    public PrintScaling Scaling
    {
        get => GetValue<PrintScaling>(ScalingProperty);
        set => SetValue(ScalingProperty, value);
    }

    /// <summary>The scale for <see cref="PrintScaling.Custom"/>, in percent.</summary>
    public double ScalePercent
    {
        get => GetValue<double>(ScalePercentProperty);
        set => SetValue(ScalePercentProperty, value);
    }

    public int PagesPerSheet
    {
        get => GetValue<int>(PagesPerSheetProperty);
        set => SetValue(PagesPerSheetProperty, value);
    }

    /// <summary>The choices of <see cref="PagesPerSheet"/>.</summary>
    public IReadOnlyList<int> PagesPerSheetChoices => PrintSettings.PagesPerSheetChoices;

    #endregion

    #region The printer's options

    public static readonly AdamantiumProperty CopiesProperty = AdamantiumProperty.Register(nameof(Copies),
        typeof(double), typeof(PrintDialog), new PropertyMetadata(1.0, OnOptionChanged));

    public static readonly AdamantiumProperty CollateProperty = AdamantiumProperty.Register(nameof(Collate),
        typeof(bool), typeof(PrintDialog), new PropertyMetadata(true, OnOptionChanged));

    public static readonly AdamantiumProperty DuplexProperty = AdamantiumProperty.Register(nameof(Duplex),
        typeof(DuplexMode), typeof(PrintDialog), new PropertyMetadata(DuplexMode.OneSided, OnOptionChanged));

    public static readonly AdamantiumProperty ColorProperty = AdamantiumProperty.Register(nameof(Color),
        typeof(PrintColorMode), typeof(PrintDialog), new PropertyMetadata(PrintColorMode.Color, OnOptionChanged));

    public static readonly AdamantiumProperty QualityProperty = AdamantiumProperty.Register(nameof(Quality),
        typeof(PrintQuality), typeof(PrintDialog), new PropertyMetadata(null, OnOptionChanged));

    public static readonly AdamantiumProperty TrayProperty = AdamantiumProperty.Register(nameof(Tray),
        typeof(string), typeof(PrintDialog), new PropertyMetadata(null, OnOptionChanged));

    public static readonly AdamantiumProperty MediaTypeProperty = AdamantiumProperty.Register(nameof(MediaType),
        typeof(string), typeof(PrintDialog), new PropertyMetadata(null, OnOptionChanged));

    public static readonly AdamantiumProperty MaxCopiesProperty = AdamantiumProperty.Register(nameof(MaxCopies),
        typeof(double), typeof(PrintDialog), new PropertyMetadata(1.0));

    public static readonly AdamantiumProperty DuplexesProperty = AdamantiumProperty.Register(nameof(Duplexes),
        typeof(IReadOnlyList<DuplexMode>), typeof(PrintDialog), new PropertyMetadata(null));

    public static readonly AdamantiumProperty ColorsProperty = AdamantiumProperty.Register(nameof(Colors),
        typeof(IReadOnlyList<PrintColorMode>), typeof(PrintDialog), new PropertyMetadata(null));

    public static readonly AdamantiumProperty QualitiesProperty = AdamantiumProperty.Register(nameof(Qualities),
        typeof(IReadOnlyList<PrintQuality>), typeof(PrintDialog), new PropertyMetadata(null));

    public static readonly AdamantiumProperty TraysProperty = AdamantiumProperty.Register(nameof(Trays),
        typeof(IReadOnlyList<string>), typeof(PrintDialog), new PropertyMetadata(null));

    public static readonly AdamantiumProperty MediaTypesProperty = AdamantiumProperty.Register(nameof(MediaTypes),
        typeof(IReadOnlyList<string>), typeof(PrintDialog), new PropertyMetadata(null));

    /// <summary>How many copies - a number box's value, so a number; printed as the whole number nearest it.</summary>
    public double Copies
    {
        get => GetValue<double>(CopiesProperty);
        set => SetValue(CopiesProperty, value);
    }

    public bool Collate
    {
        get => GetValue<bool>(CollateProperty);
        set => SetValue(CollateProperty, value);
    }

    public DuplexMode Duplex
    {
        get => GetValue<DuplexMode>(DuplexProperty);
        set => SetValue(DuplexProperty, value);
    }

    public PrintColorMode Color
    {
        get => GetValue<PrintColorMode>(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public PrintQuality Quality
    {
        get => GetValue<PrintQuality>(QualityProperty);
        set => SetValue(QualityProperty, value);
    }

    public string Tray
    {
        get => GetValue<string>(TrayProperty);
        set => SetValue(TrayProperty, value);
    }

    public string MediaType
    {
        get => GetValue<string>(MediaTypeProperty);
        set => SetValue(MediaTypeProperty, value);
    }

    /// <summary>The most copies the printer makes - the copies box's maximum.</summary>
    public double MaxCopies => GetValue<double>(MaxCopiesProperty);

    public IReadOnlyList<DuplexMode> Duplexes => GetValue<IReadOnlyList<DuplexMode>>(DuplexesProperty);

    public IReadOnlyList<PrintColorMode> Colors => GetValue<IReadOnlyList<PrintColorMode>>(ColorsProperty);

    public IReadOnlyList<PrintQuality> Qualities => GetValue<IReadOnlyList<PrintQuality>>(QualitiesProperty);

    public IReadOnlyList<string> Trays => GetValue<IReadOnlyList<string>>(TraysProperty);

    public IReadOnlyList<string> MediaTypes => GetValue<IReadOnlyList<string>>(MediaTypesProperty);

    #endregion

    #region What the printer offers, for the template

    public static readonly AdamantiumProperty ShowsCopiesProperty = Shows(nameof(ShowsCopies));
    public static readonly AdamantiumProperty ShowsCollateProperty = Shows(nameof(ShowsCollate));
    public static readonly AdamantiumProperty ShowsDuplexProperty = Shows(nameof(ShowsDuplex));
    public static readonly AdamantiumProperty ShowsColorProperty = Shows(nameof(ShowsColor));
    public static readonly AdamantiumProperty ShowsQualityProperty = Shows(nameof(ShowsQuality));
    public static readonly AdamantiumProperty ShowsTrayProperty = Shows(nameof(ShowsTray));
    public static readonly AdamantiumProperty ShowsMediaTypeProperty = Shows(nameof(ShowsMediaType));
    public static readonly AdamantiumProperty ShowsScaleProperty = Shows(nameof(ShowsScale));
    public static readonly AdamantiumProperty PrintsToFileProperty = Shows(nameof(PrintsToFile));
    public static readonly AdamantiumProperty ShowsPrinterStateProperty = Shows(nameof(ShowsPrinterState));

    public static readonly AdamantiumProperty PrinterStateProperty = AdamantiumProperty.Register(nameof(PrinterState),
        typeof(PrinterState), typeof(PrintDialog), new PropertyMetadata(PrinterState.Ready));

    public static readonly AdamantiumProperty IsAskingProperty = AdamantiumProperty.Register(nameof(IsAsking),
        typeof(bool), typeof(PrintDialog), new PropertyMetadata(false));

    private static AdamantiumProperty Shows(string name) =>
        AdamantiumProperty.Register(name, typeof(bool), typeof(PrintDialog), new PropertyMetadata(false));

    /// <summary>The printer makes copies itself.</summary>
    public bool ShowsCopies => GetValue<bool>(ShowsCopiesProperty);

    /// <summary>It makes copies and puts them in order.</summary>
    public bool ShowsCollate => GetValue<bool>(ShowsCollateProperty);

    public bool ShowsDuplex => GetValue<bool>(ShowsDuplexProperty);

    public bool ShowsColor => GetValue<bool>(ShowsColorProperty);

    public bool ShowsQuality => GetValue<bool>(ShowsQualityProperty);

    public bool ShowsTray => GetValue<bool>(ShowsTrayProperty);

    public bool ShowsMediaType => GetValue<bool>(ShowsMediaTypeProperty);

    /// <summary>The scale is asked for: <see cref="PrintScaling.Custom"/>.</summary>
    public bool ShowsScale => GetValue<bool>(ShowsScaleProperty);

    /// <summary>Printing into the PDF file - which is saved, not printed.</summary>
    public bool PrintsToFile => GetValue<bool>(PrintsToFileProperty);

    /// <summary>The printer is held up or busy - its <see cref="PrinterState"/> is worth saying.</summary>
    public bool ShowsPrinterState => GetValue<bool>(ShowsPrinterStateProperty);

    public PrinterState PrinterState => GetValue<PrinterState>(PrinterStateProperty);

    /// <summary>Whether the system is being asked - for its printers, or for what one can do.</summary>
    public bool IsAsking => GetValue<bool>(IsAskingProperty);

    #endregion

    #region The preview

    public static readonly AdamantiumProperty SheetsProperty = AdamantiumProperty.Register(nameof(Sheets),
        typeof(PrintSheets), typeof(PrintDialog), new PropertyMetadata(null));

    public static readonly AdamantiumProperty SheetProperty = AdamantiumProperty.Register(nameof(Sheet),
        typeof(int), typeof(PrintDialog), new PropertyMetadata(0, OnSheetChanged));

    public static readonly AdamantiumProperty SheetCountProperty = AdamantiumProperty.Register(nameof(SheetCount),
        typeof(int), typeof(PrintDialog), new PropertyMetadata(1));

    public static readonly AdamantiumProperty SheetNumberProperty = AdamantiumProperty.Register(nameof(SheetNumber),
        typeof(int), typeof(PrintDialog), new PropertyMetadata(1));

    /// <summary>The document laid out as chosen - what the preview shows and the printer gets.</summary>
    public PrintSheets Sheets => GetValue<PrintSheets>(SheetsProperty);

    /// <summary>The sheet the preview shows, from 0.</summary>
    public int Sheet
    {
        get => GetValue<int>(SheetProperty);
        set => SetValue(SheetProperty, value);
    }

    /// <summary><see cref="Sheet"/> counted from 1, as people count.</summary>
    public int SheetNumber => GetValue<int>(SheetNumberProperty);

    public int SheetCount => GetValue<int>(SheetCountProperty);

    #endregion

    /// <summary>The choices as they stand - fitted to the printer, put right by the system.</summary>
    public PrintSettings Settings { get; private set; } = new();

    public PrintDialogCommand PrintCommand { get; }

    public PrintDialogCommand CancelCommand { get; }

    public PrintDialogCommand PreviousSheetCommand { get; }

    public PrintDialogCommand NextSheetCommand { get; }

    /// <summary>"Print" was pressed: print <see cref="Settings"/>.</summary>
    public event EventHandler<PrintSettings> Print;

    /// <summary>The dialog was left without printing.</summary>
    public event EventHandler Canceled;

    /// <summary>A pane: the preview and the options on it say what they are.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new PaneAutomationPeer(this);

    private void OnPrint() => Print?.Invoke(this, Settings);

    private void OnCancel()
    {
        _asking?.Cancel();
        Canceled?.Invoke(this, EventArgs.Empty);
    }

    private static void OnOptionChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not PrintDialog dialog)
        {
            return;
        }

        // The user's choice is kept as asked: what a printer cannot do is shown fitted, and given back to the next
        // printer that can.
        if (!dialog._composing)
        {
            dialog.Wish(e.Property, e.NewValue);
        }

        dialog.Compose();
    }

    /// <summary>Keeps what the user asked for of <paramref name="property"/>.</summary>
    private void Wish(AdamantiumProperty property, object value)
    {
        if (property == CopiesProperty)
        {
            var copies = value is double number && double.IsFinite(number) ? (int)Math.Round(number) : 1;
            _wish = _wish with { Copies = copies };
        }
        else if (property == CollateProperty && value is bool collate)
        {
            _wish = _wish with { Collate = collate };
        }
        else if (property == DuplexProperty && value is DuplexMode duplex)
        {
            _wish = _wish with { Duplex = duplex };
        }
        else if (property == ColorProperty && value is PrintColorMode color)
        {
            _wish = _wish with { Color = color };
        }
        else if (property == QualityProperty)
        {
            _wish = _wish with { Quality = value as PrintQuality };
        }
        else if (property == TrayProperty)
        {
            _wish = _wish with { Tray = value as string };
        }
        else if (property == MediaTypeProperty)
        {
            _wish = _wish with { MediaType = value as string };
        }
        else if (property == PaperProperty && value is PaperSize paper)
        {
            _wish = _wish with { Paper = paper };
        }
        else if (property == OrientationProperty && value is PageOrientation orientation)
        {
            _wish = _wish with { Orientation = orientation };
        }
        else if (property == ScalingProperty && value is PrintScaling scaling)
        {
            _wish = _wish with { Scaling = scaling };
        }
        else if (property == ScalePercentProperty && value is double percent)
        {
            _wish = _wish with { ScalePercent = percent };
        }
        else if (property == PagesPerSheetProperty && value is int perSheet)
        {
            _wish = _wish with { PagesPerSheet = perSheet };
        }
    }

    private static void OnRangeTextChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not PrintDialog dialog)
        {
            return;
        }

        // Typing pages chooses them - which lays the sheets out anew by itself.
        if (!dialog._composing && !string.IsNullOrWhiteSpace(e.NewValue as string) &&
            dialog.RangeKind != PageRangeKind.Chosen)
        {
            dialog.RangeKind = PageRangeKind.Chosen;
            return;
        }

        dialog.Compose();
    }

    private static void OnSheetChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not PrintDialog dialog)
        {
            return;
        }

        var clamped = Math.Clamp((int)e.NewValue, 0, Math.Max(0, dialog.SheetCount - 1));
        if (clamped != (int)e.NewValue)
        {
            dialog.Sheet = clamped;
            return;
        }

        dialog.SetCurrentValue(SheetNumberProperty, clamped + 1);
        dialog.PreviousSheetCommand.RaiseCanExecuteChanged();
        dialog.NextSheetCommand.RaiseCanExecuteChanged();
    }

    private static void OnPlatformChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        _ = (component as PrintDialog)?.FindPrintersAsync();

    private static void OnPrinterChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e) =>
        _ = (component as PrintDialog)?.AskCapabilitiesAsync();

    /// <summary>Asks the platform for its printers, and chooses its default - the PDF file when there is none.</summary>
    private async Task FindPrintersAsync()
    {
        var platform = Platform;
        var asking = Restart();
        IReadOnlyList<Printer> found = [];
        if (platform != null)
        {
            Asking(true);
            try
            {
                found = await platform.FindPrintersAsync(asking.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(asking, _asking))
                {
                    Asking(false);
                }
            }
        }

        if (!ReferenceEquals(asking, _asking))
        {
            return;
        }

        // The PDF file stays first and chosen while the system's printers come in after it.
        Printer = Printer.Pdf;
        while (Printers.Count > 1)
        {
            Printers.RemoveAt(Printers.Count - 1);
        }

        foreach (var printer in found ?? [])
        {
            Printers.Add(printer);
        }

        Printer = Printers.FirstOrDefault(printer => printer.IsDefault) ?? Printer.Pdf;
    }

    /// <summary>Asks what the printer chosen can do, then shows only that.</summary>
    private async Task AskCapabilitiesAsync()
    {
        var printer = Printer ?? Printer.Pdf;
        var platform = Platform;
        var asking = Restart();
        var capabilities = PrinterCapabilities.Pdf;
        if (!printer.IsFile && platform != null)
        {
            Asking(true);
            try
            {
                capabilities = await platform.CapabilitiesAsync(printer, asking.Token) ?? PrinterCapabilities.Pdf;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                if (ReferenceEquals(asking, _asking))
                {
                    Asking(false);
                }
            }
        }

        if (!ReferenceEquals(asking, _asking))
        {
            return;
        }

        Offer(printer, capabilities);
    }

    /// <summary>A new question to the system: the one before it called off, and nothing asked until this one asks.
    /// </summary>
    private CancellationTokenSource Restart()
    {
        _asking?.Cancel();
        _asking = new CancellationTokenSource();
        Asking(false);
        return _asking;
    }

    /// <summary>Whether the system is being asked - printing waits for its answer.</summary>
    private void Asking(bool asking)
    {
        SetCurrentValue(IsAskingProperty, asking);
        PrintCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Shows what <paramref name="capabilities"/> offer, and fits the choices to them.</summary>
    internal void Offer(Printer printer, PrinterCapabilities capabilities)
    {
        _capabilities = capabilities;
        var file = printer.IsFile;

        // The lists change under their choices: whatever a list writes back meanwhile is not the user's.
        _composing = true;
        try
        {
            SetCurrentValue(PapersProperty, capabilities.Papers.Count > 0 ? capabilities.Papers : PaperSize.Standard);
            SetCurrentValue(MaxCopiesProperty, (double)Math.Max(1, capabilities.MaxCopies));
            SetCurrentValue(DuplexesProperty, capabilities.Duplex);
            SetCurrentValue(ColorsProperty, capabilities.Colors);
            SetCurrentValue(QualitiesProperty, capabilities.Qualities);
            SetCurrentValue(TraysProperty, capabilities.Trays);
            SetCurrentValue(MediaTypesProperty, capabilities.MediaTypes);
        }
        finally
        {
            _composing = false;
        }

        SetCurrentValue(ShowsCopiesProperty, capabilities.MaxCopies > 1);
        SetCurrentValue(ShowsDuplexProperty, capabilities.Duplex.Count > 1);
        SetCurrentValue(ShowsColorProperty, capabilities.Colors.Count > 1);
        SetCurrentValue(ShowsQualityProperty, capabilities.Qualities.Count > 1);
        SetCurrentValue(ShowsTrayProperty, capabilities.Trays.Count > 1);
        SetCurrentValue(ShowsMediaTypeProperty, capabilities.MediaTypes.Count > 1);
        SetCurrentValue(PrintsToFileProperty, file);
        SetCurrentValue(PrinterStateProperty, printer.State);
        SetCurrentValue(ShowsPrinterStateProperty, !file && printer.State != PrinterState.Ready);
        Compose();
    }

    /// <summary>The choices gathered, fitted to the printer and put right by the system, shown as they came back; the
    /// sheets laid out anew for the preview.</summary>
    private void Compose()
    {
        if (_composing)
        {
            return;
        }

        _composing = true;
        try
        {
            var written = PageRange.TryParse(RangeText, out var chosen);
            var range = RangeKind switch
            {
                PageRangeKind.CurrentPage => PageRange.Current,
                PageRangeKind.Chosen when written => chosen,
                _ => PageRange.All,
            };

            // Asked as the user chose, fitted to the printer and put right by the system - shown as it came back, the
            // choice itself kept.
            var settings = (_wish with { Printer = Printer ?? Printer.Pdf, Range = range }).FitTo(_capabilities);
            if (Platform is { } platform && !settings.Printer.IsFile)
            {
                settings = platform.Reconcile(_capabilities, settings) ?? settings;
            }

            Settings = settings;
            SetCurrentValue(OrientationProperty, settings.Orientation);
            SetCurrentValue(PaperProperty, settings.Paper);
            SetCurrentValue(ScalingProperty, settings.Scaling);
            SetCurrentValue(ScalePercentProperty, settings.ScalePercent);
            SetCurrentValue(PagesPerSheetProperty, settings.PagesPerSheet);
            SetCurrentValue(CopiesProperty, (double)settings.Copies);
            SetCurrentValue(CollateProperty, settings.Collate);
            SetCurrentValue(DuplexProperty, settings.Duplex);
            SetCurrentValue(ColorProperty, settings.Color);
            SetCurrentValue(QualityProperty, settings.Quality);
            SetCurrentValue(TrayProperty, settings.Tray);
            SetCurrentValue(MediaTypeProperty, settings.MediaType);
            SetCurrentValue(ShowsCollateProperty, _capabilities.Collates && settings.Copies > 1);
            SetCurrentValue(ShowsScaleProperty, settings.Scaling == PrintScaling.Custom);

            if (MovesPages(settings))
            {
                LayOut(settings);
            }

            // Pages written out must be pages, and ones the document has.
            var named = Sheets == null || Sheets.Pages.Count > 0;
            SetCurrentValue(RangeIsValidProperty, (RangeKind != PageRangeKind.Chosen || written) && named);
        }
        finally
        {
            _composing = false;
        }

        PrintCommand.RaiseCanExecuteChanged();
        PreviousSheetCommand.RaiseCanExecuteChanged();
        NextSheetCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Whether <paramref name="settings"/> put pages elsewhere than the sheets show - copies, color and the
    /// like do not.</summary>
    private bool MovesPages(PrintSettings settings) =>
        _laidOut == null || !ReferenceEquals(_laidOutDocument, Document) || _laidOutCurrentPage != CurrentPage ||
        !ReferenceEquals(_laidOutFor, _capabilities) || !Equals(_laidOut.Paper, settings.Paper) ||
        _laidOut.Orientation != settings.Orientation || _laidOut.Scaling != settings.Scaling ||
        _laidOut.ScalePercent != settings.ScalePercent || _laidOut.PagesPerSheet != settings.PagesPerSheet ||
        _laidOut.Range.Kind != settings.Range.Kind || _laidOut.Range.Text != settings.Range.Text;

    /// <summary>The sheets laid out anew for the preview, which stays among them.</summary>
    private void LayOut(PrintSettings settings)
    {
        _laidOut = settings;
        _laidOutFor = _capabilities;
        _laidOutDocument = Document;
        _laidOutCurrentPage = CurrentPage;
        if (Document is { } document)
        {
            var sheets = new PrintSheets(document, settings, _capabilities, CurrentPage);
            var count = sheets.Paginate(sheets.Sheet);
            SetCurrentValue(SheetCountProperty, count);
            SetCurrentValue(SheetsProperty, sheets);
            Sheet = Math.Clamp(Sheet, 0, count - 1);
        }
        else
        {
            SetCurrentValue(SheetsProperty, null);
            SetCurrentValue(SheetCountProperty, 1);
            Sheet = 0;
        }

        SetCurrentValue(SheetNumberProperty, Sheet + 1);
    }
}
