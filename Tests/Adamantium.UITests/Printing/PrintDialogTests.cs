using System;
using System.Collections.Generic;
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
            Assert.That(dialog.ShowsRaster, Is.True);
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
            Assert.That(dialog.ShowsRaster, Is.False);
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
}
