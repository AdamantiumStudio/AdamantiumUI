using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Core.Collections;
using Adamantium.Core.Commands;
using Adamantium.Mathematics;
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
    private const double DipsPerInch = 96;

    private readonly bool _metric = RegionInfo.CurrentRegion.IsMetric;
    private readonly IReadOnlyList<PrintMarginsChoice> _everyMargins;
    private readonly IReadOnlyList<PrintMarginsChoice> _marginsWithoutDocument;

    private PrinterCapabilities _capabilities = PrinterCapabilities.Pdf;
    private CancellationTokenSource _asking;
    private bool _composing;
    private WeakCanExecuteChangedRelay<PrintDialog> _commandRelay;

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
        PrintCommand = new PrintDialogCommand(OnPrint, () => RangeIsValid && !IsAsking && !IsPrinting && CommandCanPrint());
        CancelCommand = new PrintDialogCommand(OnCancel);
        DriverSettingsCommand = new PrintDialogCommand(OnDriverSettings, () => !IsAsking);
        PreviousSheetCommand = new PrintDialogCommand(() => Sheet--, () => Sheet > 0);
        NextSheetCommand = new PrintDialogCommand(() => Sheet++, () => Sheet < SheetCount - 1);
        MarginsUnit = _metric ? "Millimeters" : "Inches";
        MarginsStep = _metric ? 1 : 0.1;
        _everyMargins = Enum.GetValues<PrintMargins>().Select(ChoiceOf).ToArray();
        _marginsWithoutDocument = _everyMargins.Where(choice => choice.Margins != PrintMargins.Document).ToArray();
        MarginsChoices = _marginsWithoutDocument;
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

    public static readonly AdamantiumProperty IsInlineProperty = AdamantiumProperty.Register(nameof(IsInline),
        typeof(bool), typeof(PrintDialog), new PropertyMetadata(false, OnIsInlineChanged));

    public static readonly AdamantiumProperty CommandProperty = AdamantiumProperty.Register(nameof(Command),
        typeof(ICommand), typeof(PrintDialog), new PropertyMetadata(null, OnCommandChanged));

    public static readonly AdamantiumProperty IsPrintingProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(IsPrinting), typeof(bool), typeof(PrintDialog), new PropertyMetadata(false));

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

    /// <summary>Shown inside a window as a page of it - a backstage page, a page over a document - rather than in a
    /// window of its own: "Print" and the copies head the options, and leaving is the window's to offer. The document
    /// is laid out only while the page shows, anew each time it does.</summary>
    public bool IsInline
    {
        get => GetValue<bool>(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    /// <summary>Run when "Print" is pressed, with a <see cref="PrintRequest"/> of <see cref="Document"/>,
    /// <see cref="Settings"/> and <see cref="CurrentPage"/> - for a view model to print by its print service.</summary>
    public ICommand Command
    {
        get => GetValue<ICommand>(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>A print <see cref="Command"/> started is running - an <see cref="IAsyncCommand"/>, waited for: the options
    /// wait with it, as the document is being printed, and the sheets are laid out again once it ends.</summary>
    public bool IsPrinting
    {
        get => GetValue<bool>(IsPrintingProperty);
        private set => SetReadOnlyValue(IsPrintingProperty, value);
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

    public static readonly AdamantiumProperty RasterResolutionProperty = AdamantiumProperty.Register(
        nameof(RasterResolution), typeof(PrintQuality), typeof(PrintDialog),
        new PropertyMetadata(ResolutionOf(PrintSettings.DefaultRasterDotsPerInch), OnOptionChanged));

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

    /// <summary>How fine pictures of what is not vector are made in the PDF file - shown for the file alone
    /// (<see cref="PrintsToFile"/>): a printer makes them at its own resolution.</summary>
    public PrintQuality RasterResolution
    {
        get => GetValue<PrintQuality>(RasterResolutionProperty);
        set => SetValue(RasterResolutionProperty, value);
    }

    /// <summary>The choices of <see cref="RasterResolution"/>, one for each of
    /// <see cref="PrintSettings.RasterChoices"/>.</summary>
    public IReadOnlyList<PrintQuality> RasterChoices { get; } =
        PrintSettings.RasterChoices.Select(ResolutionOf).ToArray();

    private static PrintQuality ResolutionOf(int dotsPerInch) => new(dotsPerInch, dotsPerInch);

    #endregion

    #region The paper's margins

    public static readonly AdamantiumProperty MarginsProperty = AdamantiumProperty.Register(nameof(Margins),
        typeof(PrintMargins), typeof(PrintDialog), new PropertyMetadata(PrintMargins.Document, OnOptionChanged));

    public static readonly AdamantiumProperty MarginsChoicesProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(MarginsChoices), typeof(IReadOnlyList<PrintMarginsChoice>), typeof(PrintDialog),
        new PropertyMetadata(null));

    public static readonly AdamantiumProperty PaperMarginLeftProperty = MarginSide(nameof(PaperMarginLeft));
    public static readonly AdamantiumProperty PaperMarginTopProperty = MarginSide(nameof(PaperMarginTop));
    public static readonly AdamantiumProperty PaperMarginRightProperty = MarginSide(nameof(PaperMarginRight));
    public static readonly AdamantiumProperty PaperMarginBottomProperty = MarginSide(nameof(PaperMarginBottom));
    public static readonly AdamantiumProperty PaperMarginLeftMinimumProperty = MarginLimit(nameof(PaperMarginLeftMinimum));
    public static readonly AdamantiumProperty PaperMarginTopMinimumProperty = MarginLimit(nameof(PaperMarginTopMinimum));
    public static readonly AdamantiumProperty PaperMarginRightMinimumProperty = MarginLimit(nameof(PaperMarginRightMinimum));
    public static readonly AdamantiumProperty PaperMarginBottomMinimumProperty = MarginLimit(nameof(PaperMarginBottomMinimum));
    public static readonly AdamantiumProperty PaperMarginLeftMaximumProperty = MarginLimit(nameof(PaperMarginLeftMaximum));
    public static readonly AdamantiumProperty PaperMarginTopMaximumProperty = MarginLimit(nameof(PaperMarginTopMaximum));
    public static readonly AdamantiumProperty PaperMarginRightMaximumProperty = MarginLimit(nameof(PaperMarginRightMaximum));
    public static readonly AdamantiumProperty PaperMarginBottomMaximumProperty = MarginLimit(nameof(PaperMarginBottomMaximum));

    public static readonly AdamantiumProperty MarginsUnitProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(MarginsUnit), typeof(string), typeof(PrintDialog), new PropertyMetadata("Millimeters"));

    public static readonly AdamantiumProperty MarginsStepProperty = AdamantiumProperty.RegisterReadOnly(
        nameof(MarginsStep), typeof(double), typeof(PrintDialog), new PropertyMetadata(1.0));

    private static AdamantiumProperty MarginSide(string name) =>
        AdamantiumProperty.Register(name, typeof(double), typeof(PrintDialog), new PropertyMetadata(0.0, OnOptionChanged));

    private static AdamantiumProperty MarginLimit(string name) =>
        AdamantiumProperty.RegisterReadOnly(name, typeof(double), typeof(PrintDialog), new PropertyMetadata(0.0));

    /// <summary>The paper's blank edges, as chosen.</summary>
    public PrintMargins Margins
    {
        get => GetValue<PrintMargins>(MarginsProperty);
        set => SetValue(MarginsProperty, value);
    }

    /// <summary>The choices of <see cref="Margins"/>, each named in <see cref="MarginsUnit"/>: the document's own among
    /// them only for a document that has them.</summary>
    public IReadOnlyList<PrintMarginsChoice> MarginsChoices
    {
        get => GetValue<IReadOnlyList<PrintMarginsChoice>>(MarginsChoicesProperty);
        private set => SetReadOnlyValue(MarginsChoicesProperty, value);
    }

    private PrintMarginsChoice ChoiceOf(PrintMargins margins) => new(margins,
        _metric || margins is PrintMargins.Document or PrintMargins.None or PrintMargins.Custom
            ? margins.ToString()
            : margins + "Inches");

    /// <summary>The left margin, in <see cref="MarginsUnit"/>; setting a side chooses
    /// <see cref="PrintMargins.Custom"/>.</summary>
    public double PaperMarginLeft
    {
        get => GetValue<double>(PaperMarginLeftProperty);
        set => SetValue(PaperMarginLeftProperty, value);
    }

    /// <summary>The top margin, in <see cref="MarginsUnit"/>.</summary>
    public double PaperMarginTop
    {
        get => GetValue<double>(PaperMarginTopProperty);
        set => SetValue(PaperMarginTopProperty, value);
    }

    /// <summary>The right margin, in <see cref="MarginsUnit"/>.</summary>
    public double PaperMarginRight
    {
        get => GetValue<double>(PaperMarginRightProperty);
        set => SetValue(PaperMarginRightProperty, value);
    }

    /// <summary>The bottom margin, in <see cref="MarginsUnit"/>.</summary>
    public double PaperMarginBottom
    {
        get => GetValue<double>(PaperMarginBottomProperty);
        set => SetValue(PaperMarginBottomProperty, value);
    }

    /// <summary>The narrowest the left margin may be: what the printer cannot reach.</summary>
    public double PaperMarginLeftMinimum
    {
        get => GetValue<double>(PaperMarginLeftMinimumProperty);
        private set => SetReadOnlyValue(PaperMarginLeftMinimumProperty, value);
    }

    public double PaperMarginTopMinimum
    {
        get => GetValue<double>(PaperMarginTopMinimumProperty);
        private set => SetReadOnlyValue(PaperMarginTopMinimumProperty, value);
    }

    public double PaperMarginRightMinimum
    {
        get => GetValue<double>(PaperMarginRightMinimumProperty);
        private set => SetReadOnlyValue(PaperMarginRightMinimumProperty, value);
    }

    public double PaperMarginBottomMinimum
    {
        get => GetValue<double>(PaperMarginBottomMinimumProperty);
        private set => SetReadOnlyValue(PaperMarginBottomMinimumProperty, value);
    }

    /// <summary>The widest the left margin may be: room is left on the sheet for what is printed.</summary>
    public double PaperMarginLeftMaximum
    {
        get => GetValue<double>(PaperMarginLeftMaximumProperty);
        private set => SetReadOnlyValue(PaperMarginLeftMaximumProperty, value);
    }

    public double PaperMarginTopMaximum
    {
        get => GetValue<double>(PaperMarginTopMaximumProperty);
        private set => SetReadOnlyValue(PaperMarginTopMaximumProperty, value);
    }

    public double PaperMarginRightMaximum
    {
        get => GetValue<double>(PaperMarginRightMaximumProperty);
        private set => SetReadOnlyValue(PaperMarginRightMaximumProperty, value);
    }

    public double PaperMarginBottomMaximum
    {
        get => GetValue<double>(PaperMarginBottomMaximumProperty);
        private set => SetReadOnlyValue(PaperMarginBottomMaximumProperty, value);
    }

    /// <summary>The key of the unit the margins are shown in, in the print strings: "Millimeters" where the system's
    /// region measures so, "Inches" elsewhere.</summary>
    public string MarginsUnit
    {
        get => GetValue<string>(MarginsUnitProperty);
        private set => SetReadOnlyValue(MarginsUnitProperty, value);
    }

    /// <summary>How far a margin's arrows move it, in <see cref="MarginsUnit"/>.</summary>
    public double MarginsStep
    {
        get => GetValue<double>(MarginsStepProperty);
        private set => SetReadOnlyValue(MarginsStepProperty, value);
    }

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
    public static readonly AdamantiumProperty ShowsCustomMarginsProperty = Shows(nameof(ShowsCustomMargins));
    public static readonly AdamantiumProperty PrintsToFileProperty = Shows(nameof(PrintsToFile));
    public static readonly AdamantiumProperty ShowsPrinterStateProperty = Shows(nameof(ShowsPrinterState));
    public static readonly AdamantiumProperty ShowsDriverSettingsProperty = Shows(nameof(ShowsDriverSettings));

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

    /// <summary>Each side's margin is asked for: <see cref="PrintMargins.Custom"/>.</summary>
    public bool ShowsCustomMargins => GetValue<bool>(ShowsCustomMarginsProperty);

    /// <summary>Printing into the PDF file - which is saved, not printed.</summary>
    public bool PrintsToFile => GetValue<bool>(PrintsToFileProperty);

    /// <summary>The printer's driver has a window of its own for what the dialog does not offer.</summary>
    public bool ShowsDriverSettings => GetValue<bool>(ShowsDriverSettingsProperty);

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

    public static readonly AdamantiumProperty PrintCommandProperty = CommandOf(nameof(PrintCommand));
    public static readonly AdamantiumProperty CancelCommandProperty = CommandOf(nameof(CancelCommand));
    public static readonly AdamantiumProperty DriverSettingsCommandProperty = CommandOf(nameof(DriverSettingsCommand));
    public static readonly AdamantiumProperty PreviousSheetCommandProperty = CommandOf(nameof(PreviousSheetCommand));
    public static readonly AdamantiumProperty NextSheetCommandProperty = CommandOf(nameof(NextSheetCommand));

    private static AdamantiumProperty CommandOf(string name) =>
        AdamantiumProperty.RegisterReadOnly(name, typeof(PrintDialogCommand), typeof(PrintDialog),
            new PropertyMetadata(null));

    /// <summary>Prints - or saves, for the PDF file - as chosen.</summary>
    public PrintDialogCommand PrintCommand
    {
        get => GetValue<PrintDialogCommand>(PrintCommandProperty);
        private set => SetReadOnlyValue(PrintCommandProperty, value);
    }

    public PrintDialogCommand CancelCommand
    {
        get => GetValue<PrintDialogCommand>(CancelCommandProperty);
        private set => SetReadOnlyValue(CancelCommandProperty, value);
    }

    /// <summary>Opens the printer driver's own settings window.</summary>
    public PrintDialogCommand DriverSettingsCommand
    {
        get => GetValue<PrintDialogCommand>(DriverSettingsCommandProperty);
        private set => SetReadOnlyValue(DriverSettingsCommandProperty, value);
    }

    public PrintDialogCommand PreviousSheetCommand
    {
        get => GetValue<PrintDialogCommand>(PreviousSheetCommandProperty);
        private set => SetReadOnlyValue(PreviousSheetCommandProperty, value);
    }

    public PrintDialogCommand NextSheetCommand
    {
        get => GetValue<PrintDialogCommand>(NextSheetCommandProperty);
        private set => SetReadOnlyValue(NextSheetCommandProperty, value);
    }

    /// <summary>"Print" was pressed: print <see cref="Settings"/>.</summary>
    public event EventHandler<PrintSettings> Print;

    /// <summary>The dialog was left without printing.</summary>
    public event EventHandler Canceled;

    /// <summary>Shown inside a window, the document is laid out anew each time the page shows: it may have changed
    /// since it last did.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsInline)
        {
            _laidOut = null;
            Compose();
        }
    }

    /// <summary>A pane: the preview and the options on it say what they are.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new PaneAutomationPeer(this);

    private void OnPrint()
    {
        Print?.Invoke(this, Settings);
        if (Document == null || Command is not { } command)
        {
            return;
        }

        var request = new PrintRequest(Document, Settings, CurrentPage);
        if (!command.CanExecute(request))
        {
            return;
        }

        if (command is IAsyncCommand running)
        {
            _ = PrintThroughAsync(running, request);
        }
        else
        {
            command.Execute(request);
        }
    }

    /// <summary>Runs <paramref name="command"/> and waits for it, the options held meanwhile; the sheets laid out again
    /// after, as printing laid the document out for the printer.</summary>
    private async Task PrintThroughAsync(IAsyncCommand command, PrintRequest request)
    {
        IsPrinting = true;
        PrintCommand.RaiseCanExecuteChanged();
        try
        {
            await command.ExecuteAsync(request);
        }
        catch (Exception exception)
        {
            Serilog.Log.Logger.Error(exception, "Printing {Title} failed.", request.Document.Title);
        }
        finally
        {
            IsPrinting = false;
            _laidOut = null;
            Compose();
        }
    }

    private bool CommandCanPrint() =>
        Document == null || Command is not { } command ||
        command.CanExecute(new PrintRequest(Document, Settings, CurrentPage));

    private static void OnIsInlineChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is PrintDialog dialog)
        {
            dialog._laidOut = null;
            dialog.Compose();
        }
    }

    private static void OnCommandChanged(AdamantiumComponent component, AdamantiumPropertyChangedEventArgs e)
    {
        if (component is not PrintDialog dialog)
        {
            return;
        }

        dialog._commandRelay?.Detach();
        dialog._commandRelay = e.NewValue is ICommand command
            ? new WeakCanExecuteChangedRelay<PrintDialog>(command, dialog, static target =>
                target.PrintCommand.RaiseCanExecuteChanged())
            : null;
        dialog.PrintCommand?.RaiseCanExecuteChanged();
    }

    /// <summary>The driver's window: what it is left with becomes the user's choice - the dialog's options as it set
    /// them, its own kept for the job.</summary>
    private void OnDriverSettings()
    {
        if (Platform is not { } platform || !_capabilities.HasDriverSettings)
        {
            return;
        }

        var edited = platform.EditDriverSettings(_capabilities, Settings);
        if (edited == null)
        {
            return;
        }

        _wish = edited with { Printer = _wish.Printer, Range = _wish.Range };
        Compose();
    }

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
        else if (property == RasterResolutionProperty && value is PrintQuality raster)
        {
            _wish = _wish with { RasterDotsPerInch = raster.DotsPerInchX };
        }
        else if (property == MarginsProperty && value is PrintMargins margins)
        {
            var starting = margins == PrintMargins.Custom && Settings.Margins != PrintMargins.Custom;
            _wish = _wish with { Margins = margins, CustomMargins = starting ? ShownMargins() : _wish.CustomMargins };
        }
        else if (property == PaperMarginLeftProperty || property == PaperMarginTopProperty || property == PaperMarginRightProperty ||
                 property == PaperMarginBottomProperty)
        {
            _wish = _wish with { Margins = PrintMargins.Custom, CustomMargins = ShownMargins() };
        }
    }

    /// <summary>The margins the boxes show, in DIPs.</summary>
    private Thickness ShownMargins() => new(PaperMarginLeft * DipsPerUnit, PaperMarginTop * DipsPerUnit,
        PaperMarginRight * DipsPerUnit, PaperMarginBottom * DipsPerUnit);

    private double DipsPerUnit => _metric ? PaperSize.DipsPerMillimeter : DipsPerInch;

    /// <summary>A length in DIPs as the boxes show it: to a tenth of a millimeter, or a hundredth of an inch, rounded
    /// <paramref name="rounding"/> - a limit up or down, so it stays within what the sheet allows.</summary>
    private double UnitsOf(double dips, MidpointRounding rounding = MidpointRounding.ToEven)
    {
        var scale = _metric ? 10 : 100;
        return Math.Round(dips / DipsPerUnit * scale, rounding) / scale;
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
            catch (Exception)
            {
                // The system could not list its printers: the PDF file is still offered.
                found = [];
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
            catch (Exception)
            {
                // The system could not say what the printer does: none of its own options are offered, only the
                // platform's, which every printer takes.
                capabilities = new PrinterCapabilities();
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
        DriverSettingsCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Shows what <paramref name="capabilities"/> offer, and fits the choices to them.</summary>
    internal void Offer(Printer printer, PrinterCapabilities capabilities)
    {
        _capabilities = capabilities;
        var file = printer.IsFile;

        // The driver's own settings were another printer's.
        _wish = _wish with { DriverSettings = null };

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
        SetCurrentValue(ShowsDriverSettingsProperty, !file && capabilities.HasDriverSettings);
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

            var ownMargins = Document?.HasOwnMargins == true;
            if (settings.Margins == PrintMargins.Document && !ownMargins)
            {
                settings = settings with { Margins = PrintMargins.Normal };
            }

            Settings = settings;
            MarginsChoices = ownMargins ? _everyMargins : _marginsWithoutDocument;
            SetCurrentValue(MarginsProperty, settings.Margins);
            SetCurrentValue(ShowsCustomMarginsProperty, settings.Margins == PrintMargins.Custom);
            SetCurrentValue(OrientationProperty, settings.Orientation);
            SetCurrentValue(PaperProperty, settings.Paper);
            SetCurrentValue(ScalingProperty, settings.Scaling);
            SetCurrentValue(ScalePercentProperty, settings.ScalePercent);
            SetCurrentValue(PagesPerSheetProperty, settings.PagesPerSheet);
            SetCurrentValue(RasterResolutionProperty, ResolutionOf(settings.RasterDotsPerInch));
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

            ShowMargins(settings);

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
        _laidOut.Range.Kind != settings.Range.Kind || _laidOut.Range.Text != settings.Range.Text ||
        _laidOut.PaperMargins != settings.PaperMargins;

    /// <summary>The margins of the sheets laid out in the boxes, each between what the printer reaches and what leaves
    /// room on the sheet.</summary>
    private void ShowMargins(PrintSettings settings)
    {
        if (Sheets is not { } sheets)
        {
            return;
        }

        var sheet = sheets.Sheet;
        var least = sheet.PrintableMargins;
        var most = sheet.MaximumMargins;
        PaperMarginLeftMinimum = UnitsOf(least.Left, MidpointRounding.ToPositiveInfinity);
        PaperMarginTopMinimum = UnitsOf(least.Top, MidpointRounding.ToPositiveInfinity);
        PaperMarginRightMinimum = UnitsOf(least.Right, MidpointRounding.ToPositiveInfinity);
        PaperMarginBottomMinimum = UnitsOf(least.Bottom, MidpointRounding.ToPositiveInfinity);
        PaperMarginLeftMaximum = UnitsOf(most.Left, MidpointRounding.ToNegativeInfinity);
        PaperMarginTopMaximum = UnitsOf(most.Top, MidpointRounding.ToNegativeInfinity);
        PaperMarginRightMaximum = UnitsOf(most.Right, MidpointRounding.ToNegativeInfinity);
        PaperMarginBottomMaximum = UnitsOf(most.Bottom, MidpointRounding.ToNegativeInfinity);

        var shown = sheet.Margins ?? settings.CustomMargins;
        SetCurrentValue(PaperMarginLeftProperty, UnitsOf(shown.Left));
        SetCurrentValue(PaperMarginTopProperty, UnitsOf(shown.Top));
        SetCurrentValue(PaperMarginRightProperty, UnitsOf(shown.Right));
        SetCurrentValue(PaperMarginBottomProperty, UnitsOf(shown.Bottom));
    }

    /// <summary>The sheets laid out anew for the preview, which stays among them.</summary>
    private void LayOut(PrintSettings settings)
    {
        _laidOut = settings;
        _laidOutFor = _capabilities;
        _laidOutDocument = Document;
        _laidOutCurrentPage = CurrentPage;
        if (IsInline && !IsAttachedToVisualTree)
        {
            _laidOut = null;
            SetCurrentValue(SheetsProperty, null);
            SetCurrentValue(SheetCountProperty, 1);
        }
        else if (Document is { } document)
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
