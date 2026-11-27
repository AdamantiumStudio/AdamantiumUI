using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.UI.Controls.Printing;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Printing;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>The print dialog's logic, without a window: what it offers comes from the printer chosen, the system puts
/// choices right and the dialog shows what came back, a range that is no pages stops printing, and the sheets follow the
/// choices.</summary>
public class PrintDialogTests
{
    private sealed class Pages(int count) : IPrintDocument
    {
        public string Title => "Pages";

        public int Paginate(PrintPage page) => count;

        public void DrawPage(int index, IDrawingSession session)
        {
        }
    }

    private sealed class Platform : IPrintPlatform
    {
        public static readonly Printer Laser = new("laser", "Laser", isDefault: true);
        public static readonly Printer Photo = new("photo", "Photo", state: PrinterState.OutOfPaper);

        public Task<IReadOnlyList<Printer>> FindPrintersAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<Printer>>([Laser, Photo]);

        public Task<PrinterCapabilities> CapabilitiesAsync(Printer printer, CancellationToken cancellation) =>
            Task.FromResult(printer == Laser
                ? new PrinterCapabilities
                {
                    MaxCopies = 99,
                    Collates = true,
                    Duplex = [DuplexMode.OneSided, DuplexMode.LongEdge],
                    Colors = [PrintColorMode.Grayscale],
                    HasDriverSettings = true,
                }
                : new PrinterCapabilities
                {
                    Duplex = [DuplexMode.OneSided, DuplexMode.LongEdge],
                    Colors = [PrintColorMode.Color, PrintColorMode.Grayscale],
                    MediaTypes = ["Plain", "Glossy"],
                });

        // Glossy paper is printed on one side only, as a driver would say.
        public PrintSettings Reconcile(PrinterCapabilities capabilities, PrintSettings settings) =>
            settings.MediaType == "Glossy" ? settings with { Duplex = DuplexMode.OneSided } : settings;

        /// <summary>What a driver's window is left with: both sides chosen in it, and its own setting.</summary>
        public object Answer { get; set; } = "stapled";

        public PrintSettings EditDriverSettings(PrinterCapabilities capabilities, PrintSettings settings) =>
            Answer == null ? null : settings with { Duplex = DuplexMode.LongEdge, DriverSettings = Answer };

        public Task PrintAsync(IPrintDocument sheets, PrintSettings settings, IProgress<PrintProgress> progress,
            CancellationToken cancellation) => Task.CompletedTask;
    }

    [Test]
    public void WithoutPrinters_ThePdfFileAlone_IsOffered_WithItsOwnOptionsOnly()
    {
        var dialog = new PrintDialog { Document = new Pages(3) };

        Assert.Multiple(() =>
        {
            Assert.That(dialog.Printers, Is.EqualTo(new[] { Printer.Pdf }));
            Assert.That(dialog.Printer, Is.SameAs(Printer.Pdf));
            Assert.That(dialog.PrintsToFile, Is.True);
            Assert.That(dialog.ShowsCopies, Is.False);
            Assert.That(dialog.ShowsDuplex, Is.False);
            Assert.That(dialog.ShowsColor, Is.False);
            Assert.That(dialog.SheetCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void ThePrintersDefault_IsChosen_AndWhatItOffersIsShown()
    {
        var dialog = new PrintDialog { Document = new Pages(3), Platform = new Platform() };

        Assert.Multiple(() =>
        {
            Assert.That(dialog.Printers, Is.EqualTo(new[] { Printer.Pdf, Platform.Laser, Platform.Photo }));
            Assert.That(dialog.Printer, Is.SameAs(Platform.Laser));
            Assert.That(dialog.ShowsCopies, Is.True);
            Assert.That(dialog.ShowsDuplex, Is.True);
            Assert.That(dialog.ShowsColor, Is.False, "gray only: nothing to choose");
            Assert.That(dialog.Color, Is.EqualTo(PrintColorMode.Grayscale));
            Assert.That(dialog.PrintsToFile, Is.False);
            Assert.That(dialog.ShowsPrinterState, Is.False);
        });
    }

    [Test]
    public void AnotherPrinter_ShowsWhatItOffers_AndWhatItsStateIs()
    {
        var dialog = new PrintDialog { Document = new Pages(3), Platform = new Platform() };
        dialog.Copies = 5;
        dialog.Printer = Platform.Photo;

        Assert.Multiple(() =>
        {
            Assert.That(dialog.ShowsCopies, Is.False);
            Assert.That(dialog.Copies, Is.EqualTo(1), "it makes no copies itself");
            Assert.That(dialog.ShowsColor, Is.True);
            Assert.That(dialog.ShowsMediaType, Is.True);
            Assert.That(dialog.ShowsPrinterState, Is.True);
            Assert.That(dialog.PrinterState, Is.EqualTo(PrinterState.OutOfPaper));
        });
    }

    [Test]
    public void CopiesAndTheirOrder_ShowTogether()
    {
        var dialog = new PrintDialog { Document = new Pages(3), Platform = new Platform() };
        Assert.That(dialog.ShowsCollate, Is.False, "one copy has no order");

        dialog.Copies = 3;
        Assert.That(dialog.ShowsCollate, Is.True);
        Assert.That(dialog.Settings.Copies, Is.EqualTo(3));
    }

    // What the system puts right is what the dialog shows.
    [Test]
    public void AChoiceTheSystemPutsRight_IsShownAsItCameBack()
    {
        var dialog = new PrintDialog { Document = new Pages(3), Platform = new Platform() };
        dialog.Printer = Platform.Photo;
        dialog.Duplex = DuplexMode.LongEdge;
        Assert.That(dialog.Duplex, Is.EqualTo(DuplexMode.LongEdge));

        dialog.MediaType = "Glossy";
        Assert.That(dialog.Duplex, Is.EqualTo(DuplexMode.OneSided));
        Assert.That(dialog.Settings.Duplex, Is.EqualTo(DuplexMode.OneSided));
    }

    [Test]
    public void PagesWrittenOut_ChooseThem_AndOnesThatAreNoPages_StopPrinting()
    {
        var dialog = new PrintDialog { Document = new Pages(6) };

        dialog.RangeText = "2-3";
        Assert.That(dialog.RangeKind, Is.EqualTo(PageRangeKind.Chosen), "typing pages chooses them");
        Assert.That(dialog.SheetCount, Is.EqualTo(2));
        Assert.That(dialog.PrintCommand.CanExecute(), Is.True);

        dialog.RangeText = "3-2";
        Assert.That(dialog.RangeIsValid, Is.False);
        Assert.That(dialog.PrintCommand.CanExecute(), Is.False);

        dialog.RangeKind = PageRangeKind.All;
        Assert.That(dialog.RangeIsValid, Is.True, "all pages need no numbers");
        Assert.That(dialog.SheetCount, Is.EqualTo(6));
    }

    // A choice one printer cannot make is not forgotten: it comes back with the next printer that can.
    [Test]
    public void AChoiceAPrinterCannotMake_ComesBackWithOneThatCan()
    {
        var dialog = new PrintDialog { Document = new Pages(3), Platform = new Platform() };
        Assert.That(dialog.Collate, Is.True, "copies in order, as asked by default");

        dialog.Copies = 5;
        dialog.Printer = Platform.Photo;
        Assert.That(dialog.Copies, Is.EqualTo(1));
        Assert.That(dialog.Collate, Is.False, "it does not collate");

        dialog.Printer = Platform.Laser;
        Assert.That(dialog.Copies, Is.EqualTo(5));
        Assert.That(dialog.Collate, Is.True);
    }

    [Test]
    public void PagesTheDocumentHasNot_AreNoPages_AndStopPrinting()
    {
        var dialog = new PrintDialog { Document = new Pages(3) };

        dialog.RangeText = "5-9";
        Assert.That(dialog.RangeIsValid, Is.False);
        Assert.That(dialog.PrintCommand.CanExecute(), Is.False);

        dialog.RangeText = "2-9";
        Assert.That(dialog.RangeIsValid, Is.True, "page 2 and 3 are there");
        Assert.That(dialog.SheetCount, Is.EqualTo(2));
    }

    /// <summary>A platform whose answer about a printer is held until the test lets it go.</summary>
    private sealed class SlowPlatform : IPrintPlatform
    {
        public TaskCompletionSource<PrinterCapabilities> Answer { get; private set; } = new();

        public Task<IReadOnlyList<Printer>> FindPrintersAsync(CancellationToken cancellation) =>
            Task.FromResult<IReadOnlyList<Printer>>([Platform.Laser, Platform.Photo]);

        public Task<PrinterCapabilities> CapabilitiesAsync(Printer printer, CancellationToken cancellation)
        {
            Answer = new TaskCompletionSource<PrinterCapabilities>();
            cancellation.Register(() => Answer.TrySetCanceled());
            return Answer.Task;
        }

        public PrintSettings Reconcile(PrinterCapabilities capabilities, PrintSettings settings) => settings;

        public PrintSettings EditDriverSettings(PrinterCapabilities capabilities, PrintSettings settings) => null;

        public Task PrintAsync(IPrintDocument sheets, PrintSettings settings, IProgress<PrintProgress> progress,
            CancellationToken cancellation) => Task.CompletedTask;
    }

    [Test]
    public void WhileThePrinterIsAsked_PrintingWaits_AndTheFileNeedsNoAnswer()
    {
        var platform = new SlowPlatform();
        var dialog = new PrintDialog { Document = new Pages(3), Platform = platform };
        Assert.That(dialog.IsAsking, Is.True);
        Assert.That(dialog.PrintCommand.CanExecute(), Is.False, "not with what another printer could do");

        platform.Answer.SetResult(new PrinterCapabilities { MaxCopies = 9 });
        Assert.That(dialog.IsAsking, Is.False);
        Assert.That(dialog.PrintCommand.CanExecute(), Is.True);

        // Asked about one printer, then the file chosen: nothing is asked any more.
        dialog.Printer = Platform.Photo;
        Assert.That(dialog.IsAsking, Is.True);
        dialog.Printer = Printer.Pdf;
        Assert.That(dialog.IsAsking, Is.False);
        Assert.That(dialog.PrintCommand.CanExecute(), Is.True);
    }

    [Test]
    public void TheSheets_FollowTheChoices_AndThePreviewStaysAmongThem()
    {
        var dialog = new PrintDialog { Document = new Pages(9) };
        dialog.Sheet = 8;
        Assert.That(dialog.SheetNumber, Is.EqualTo(9));

        dialog.PagesPerSheet = 4;
        Assert.That(dialog.SheetCount, Is.EqualTo(3));
        Assert.That(dialog.Sheet, Is.EqualTo(2), "the preview kept among the sheets there are");
        Assert.That(dialog.NextSheetCommand.CanExecute(), Is.False);
        Assert.That(dialog.PreviousSheetCommand.CanExecute(), Is.True);
    }

    [Test]
    public void TheFilesRasterResolution_IsChosenInTheDialog_AndReachesTheSheet()
    {
        var dialog = new PrintDialog { Document = new Pages(3) };
        Assert.That(dialog.RasterResolution, Is.EqualTo(new PrintQuality(300, 300)), "300 dpi unless chosen");

        dialog.RasterResolution = dialog.RasterChoices[^1];

        Assert.Multiple(() =>
        {
            Assert.That(dialog.PrintsToFile, Is.True, "the row shows for the file");
            Assert.That(dialog.Settings.RasterDotsPerInch, Is.EqualTo(600));
            Assert.That(new PrintSheets(dialog.Document, dialog.Settings, PrinterCapabilities.Pdf).Sheet.RasterDotsPerInch,
                Is.EqualTo(600));
        });
    }

    // The driver's own window: offered where the printer has one; what it is left with is printed - the dialog's options
    // as it set them, its own kept - and another printer starts without them.
    [Test]
    public void ThePrintersOwnSettings_AreOfferedWhereItHasThem_AndWhatTheyAreLeftWithIsPrinted()
    {
        var platform = new Platform();
        var dialog = new PrintDialog { Document = new Pages(3), Platform = platform };
        Assert.That(dialog.ShowsDriverSettings, Is.True);

        dialog.DriverSettingsCommand.Execute();
        Assert.That(dialog.Duplex, Is.EqualTo(DuplexMode.LongEdge), "chosen in the driver's window");
        Assert.That(dialog.Settings.DriverSettings, Is.EqualTo("stapled"));

        platform.Answer = null;
        dialog.DriverSettingsCommand.Execute();
        Assert.That(dialog.Settings.DriverSettings, Is.EqualTo("stapled"), "canceled: nothing changes");

        dialog.Printer = Platform.Photo;
        Assert.That(dialog.ShowsDriverSettings, Is.False);
        Assert.That(dialog.Settings.DriverSettings, Is.Null, "the laser's own settings are not the photo printer's");

        dialog.Printer = Printer.Pdf;
        Assert.That(dialog.ShowsDriverSettings, Is.False, "the PDF file has no driver");
    }

    // Copies, color and the like print the same sheets: only what moves pages lays them out again.
    [Test]
    public void OnlyWhatMovesPages_LaysTheSheetsOutAgain()
    {
        var dialog = new PrintDialog { Document = new Pages(3), Platform = new Platform() };
        var sheets = dialog.Sheets;

        dialog.Copies = 3;
        dialog.Duplex = DuplexMode.LongEdge;
        Assert.That(dialog.Sheets, Is.SameAs(sheets));

        dialog.Orientation = PageOrientation.Landscape;
        Assert.That(dialog.Sheets, Is.Not.SameAs(sheets));
    }

    [Test]
    public void Print_HandsOverTheSettings_Cancel_Says_ItWasLeft()
    {
        var dialog = new PrintDialog { Document = new Pages(2), Platform = new Platform() };
        PrintSettings printed = null;
        var canceled = false;
        dialog.Print += (_, settings) => printed = settings;
        dialog.Canceled += (_, _) => canceled = true;
        dialog.Copies = 2.4;

        dialog.PrintCommand.Execute();
        dialog.CancelCommand.Execute();

        Assert.That(printed?.Printer, Is.SameAs(Platform.Laser));
        Assert.That(printed?.Copies, Is.EqualTo(2), "a whole number of copies");
        Assert.That(canceled, Is.True);
    }

    // Shown inside a window, "Print" hands a view model's command what to print and how.
    [Test]
    public void Print_RunsTheCommand_WithTheDocumentTheSettingsAndTheCurrentPage()
    {
        var document = new Pages(4);
        var command = new RecordingCommand();
        var dialog = new PrintDialog { Document = document, CurrentPage = 2, IsInline = true, Command = command };

        dialog.PrintCommand.Execute();

        Assert.That(command.Parameter, Is.EqualTo(new PrintRequest(document, dialog.Settings, 2)));
    }

    // A print a command runs holds the options until it ends; then the sheets are laid out again.
    [Test]
    public void WhileACommandPrints_TheOptionsWait_AndTheSheetsAreLaidOutAgainAfter()
    {
        var printing = new TaskCompletionSource();
        var dialog = new PrintDialog { Document = new Pages(3), IsInline = true };
        dialog.Command = new Adamantium.MVVM.AdamantiumAsyncCommand(_ => printing.Task);
        var sheets = dialog.Sheets;

        dialog.PrintCommand.Execute();
        Assert.That(dialog.IsPrinting, Is.True);
        Assert.That(dialog.PrintCommand.CanExecute(), Is.False, "not twice at once");

        printing.SetResult();
        Assert.Multiple(() =>
        {
            Assert.That(dialog.IsPrinting, Is.False);
            Assert.That(dialog.PrintCommand.CanExecute(), Is.True);
            Assert.That(dialog.Sheets, Is.Not.SameAs(sheets), "laid out again for the preview");
        });
    }

    // "As in the document" is offered only for a document with margins of its own; one without starts with the normal.
    [Test]
    public void TheDocumentsOwnMargins_AreOfferedOnlyWhereItHasThem()
    {
        var plain = new PrintDialog { Document = new Pages(2) };
        var own = new PrintDialog { Document = new PreparedPagesDocument(2, hasOwnMargins: true) };

        Assert.Multiple(() =>
        {
            Assert.That(plain.MarginsChoices.Select(choice => choice.Margins), Does.Not.Contain(PrintMargins.Document));
            Assert.That(plain.Margins, Is.EqualTo(PrintMargins.Normal));
            Assert.That(own.MarginsChoices.Select(choice => choice.Margins), Does.Contain(PrintMargins.Document));
            Assert.That(own.Margins, Is.EqualTo(PrintMargins.Document));
            Assert.That(own.Sheets.Sheet.Margins, Is.Null, "its own kept");
        });
    }

    // A side set by hand chooses custom margins, starting from those shown; the sheets are laid out inside them.
    [Test]
    public void ASideSetByHand_ChoosesCustomMargins_StartingFromThoseShown()
    {
        var dialog = new PrintDialog { Document = new Pages(2) };
        var unit = dialog.MarginsUnit == "Millimeters" ? 96 / 25.4 : 96;
        Assert.That(dialog.ShowsCustomMargins, Is.False);

        dialog.PaperMarginLeft = 2 * dialog.PaperMarginLeft;

        var normal = PrintSettings.MarginsOf(PrintMargins.Normal).Value;
        var laid = dialog.Sheets.Sheet.Margins.Value;
        Assert.Multiple(() =>
        {
            Assert.That(dialog.Margins, Is.EqualTo(PrintMargins.Custom));
            Assert.That(dialog.ShowsCustomMargins, Is.True);
            Assert.That(laid.Left, Is.EqualTo(2 * normal.Left).Within(unit / 10), "the side set");
            Assert.That(laid.Top, Is.EqualTo(normal.Top).Within(unit / 10), "the others as they were shown");
        });
    }

    // Each side's box stops at what the printer reaches and at what leaves room on the sheet.
    [Test]
    public void EachSidesBox_IsKeptWithinTheSheet()
    {
        var dialog = new PrintDialog { Document = new Pages(2), Paper = PaperSize.A4 };
        var unit = dialog.MarginsUnit == "Millimeters" ? 96 / 25.4 : 96;

        Assert.Multiple(() =>
        {
            Assert.That(dialog.PaperMarginLeftMinimum, Is.EqualTo(0), "the PDF file reaches every edge");
            Assert.That(dialog.PaperMarginLeftMaximum * unit, Is.LessThanOrEqualTo((PaperSize.A4.Width - 96) / 2));
            Assert.That(dialog.PaperMarginTopMaximum * unit, Is.LessThanOrEqualTo((PaperSize.A4.Height - 96) / 2));
            Assert.That(dialog.PaperMarginTopMaximum, Is.GreaterThan(dialog.PaperMarginLeftMaximum), "upright");
        });
    }
}
