using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Core.Rendering;
using Adamantium.UI.Printing;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>What is not vector is printed as pictures the GPU makes ahead, at the sheet's raster resolution, unfaded:
/// an element's shadows and glows are pictures of their own - the outer under it, the inner between its fills and its
/// text - and the element stays vector, its text too; one showing a live surface has its own drawing printed as a
/// picture; a soft edge or a reflection makes all it holds a picture. They are faded as shapes are, made once a layout
/// and resolution, in squares the GPU can draw, and freed with the document.</summary>
public class ElementRasterPrintTests
{
    // 192 dpi: two device pixels to the DIP.
    private static readonly PrintPage Sheet = new(new PaperSize("Test", 400, 300), PageOrientation.Portrait,
        new Thickness(20, 30, 20, 30)) { RasterDotsPerInch = 192 };

    private static Border Card(out Border inner)
    {
        inner = new Border
        {
            Width = 50,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Colors.Blue),
        };
        return new Border
        {
            Width = 200, Height = 100, Background = new SolidColorBrush(Colors.Red), Child = inner,
            Shadow = new Shadow(),
        };
    }

    private static async Task<RecordingDrawingSession> PrintAsync(ElementPrintDocument document, PrintPage sheet = null)
    {
        var session = new RecordingDrawingSession();
        document.Paginate(sheet ?? Sheet);
        await document.PrepareAsync(sheet ?? Sheet, CancellationToken.None);
        document.DrawPage(0, session);
        return session;
    }

    [Test]
    public async Task AShadowedElement_HasItsShadowPrintedAsAPicture_AndItselfAndWhatItHoldsStayVector()
    {
        var card = Card(out _);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(card, "Card", ElementFit.Width, gpu);

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked, Has.Count.EqualTo(1), "one picture, of the card's shadow");
            Assert.That(gpu.Asked[0].Content, Is.EqualTo(SnapshotContent.BandsUnder), "the shadow alone, under it");
            Assert.That(gpu.Asked[0].Region, Is.EqualTo(new Rect(-12, -8, 224, 124)),
                "the card grown by its shadow: 12 blur round it, thrown 4 down");
            Assert.That(gpu.Asked[0].Scale, Is.EqualTo(2), "192 dpi");
            Assert.That(session.PlacedImages.Single().Destination, Is.EqualTo(new Rect(-12, -8, 224, 124)));
            Assert.That(session.Geometries.Select(entry => (entry.Brush as SolidColorBrush)?.Color),
                Is.EqualTo(new Color?[] { Colors.Red, Colors.Blue }), "the card and the box, from their shapes");
        });
    }

    [Test]
    public async Task AClippedElementsOuterShadow_IsAPictureOfItsBox_WhereItShowsThroughItsFill()
    {
        var card = Card(out _);
        card.ClipToBounds = true;
        card.Background = new SolidColorBrush(new Color(255, 0, 0, 128));
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(card, null, ElementFit.Width, gpu);

        await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Single().Content, Is.EqualTo(SnapshotContent.BandsUnder));
            Assert.That(gpu.Asked.Single().Region, Is.EqualTo(new Rect(0, 0, 200, 100)), "cut to its box by its clip");
        });
    }

    [Test]
    public async Task TextWithAShadow_StaysVector_OverItsShadowsPicture()
    {
        var heading = new Adamantium.UI.Controls.Text.TextBlock
        {
            Text = "A heading", FontSize = 28, Shadow = new Shadow { BlurRadius = 4, OffsetY = 2 },
        };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(heading);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(heading, null, ElementFit.Width, gpu);

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Single().Content, Is.EqualTo(SnapshotContent.BandsUnder));
            Assert.That(session.PlacedImages, Has.Count.EqualTo(1), "the shadow, as its picture");
            Assert.That(session.Texts, Has.Count.EqualTo(1), "the heading itself, as text - selectable in the PDF");
        });
    }

    [Test]
    public async Task ATextsShadowAndGlow_AreAPictureUnderTheText_AndTheTextStaysVector()
    {
        var heading = new Adamantium.UI.Controls.Text.TextBlock
        {
            Text = "A heading", FontSize = 28, TextShadow = new TextShadow { Blur = 3 },
            TextGlow = new TextGlow { Radius = 2 },
        };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(heading);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(heading, null, ElementFit.Width, gpu);

        var session = await PrintAsync(document);

        var screen = new RecordingDrawingSession { DrawsGlyphEffects = true };
        document.DrawPage(0, screen);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Single().Content, Is.EqualTo(SnapshotContent.GlyphEffects),
                "the glyphs' shadow and glow alone, the glyphs left out of the picture");
            Assert.That(session.PlacedImages, Has.Count.EqualTo(1));
            Assert.That(session.Texts, Has.Count.EqualTo(1), "the heading itself, as text");
            Assert.That(screen.PlacedImages, Is.Empty, "a screen draws the effects with the text: no second shadow");
            Assert.That(screen.Texts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AnInnerShadow_IsLaidBetweenTheFillsAndTheText_EachDrawnOnce()
    {
        var field = new Adamantium.UI.Controls.Text.TextBlock
        {
            Text = "Recessed", FontSize = 20, Background = new SolidColorBrush(Colors.LightGray),
            Shadow = new Shadow { Inner = true, BlurRadius = 6, OffsetY = 2 },
        };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(field);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(field, null, ElementFit.Width, gpu);

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Single().Content, Is.EqualTo(SnapshotContent.BandsOver), "inside it: no outer picture");
            Assert.That(gpu.Asked.Single().Region.X, Is.Zero, "an inner band never leaves its box");
            Assert.That(session.PlacedImages, Has.Count.EqualTo(1));
            Assert.That(session.Texts, Has.Count.EqualTo(1), "the text once, in the second go");
        });
    }

    [Test]
    public async Task ASoftEdgedElement_IsPrintedAsAPictureOfItAndAllItHolds_FadedAsAWhole()
    {
        var card = Card(out _);
        card.Shadow = null;
        card.SoftEdge = 6;
        card.Opacity = 0.5;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(card, null, ElementFit.Width, gpu);

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Single().Content, Is.EqualTo(SnapshotContent.Subtree), "the soft edge reaches the box too");
            Assert.That(session.Geometries, Is.Empty, "nothing of it from its shapes");
            Assert.That(session.PlacedImages, Has.Count.EqualTo(1));
            Assert.That(session.Groups.Select(group => group.Opacity).Where(opacity => opacity < 1),
                Is.EqualTo(new[] { 0.5 }), "laid down at its own fade, as its layer is on the screen");
        });
    }

    [Test]
    public async Task APicture_IsFadedAsShapesAre_AndWhatIsNotInOneIsDrawnFromItsShapes()
    {
        var card = Card(out _);
        var panel = new StackPanel { Opacity = 0.5 };
        ((IContainer)panel).AddOrSetChildComponent(new Border
            { Width = 200, Height = 30, Background = new SolidColorBrush(Colors.Green) });
        ((IContainer)panel).AddOrSetChildComponent(card);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(panel);
        using var document = new ElementPrintDocument(panel, null, ElementFit.Width, new SnapshotRecorder());

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(session.Geometries.Select(entry => (entry.Brush as SolidColorBrush)?.Color),
                Is.EquivalentTo(new Color?[] { Colors.Transparent, Colors.Green, Colors.Red, Colors.Blue }),
                "the panel's empty background, the strip, the card and the box in it, from their shapes");
            Assert.That(session.PlacedImages, Has.Count.EqualTo(1), "the card's shadow, as its picture");
            Assert.That(session.Groups.Count(group => group.Opacity == 0.5), Is.EqualTo(4),
                "the panel's own drawing, the strip's, the card's - its shadow in it - and the box, each faded by the panel");
        });
    }

    [Test]
    public async Task AnElementShowingALiveSurface_IsPrintedAsItsPicture()
    {
        var view = new SurfaceView { Width = 120, Height = 80 };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(view);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(view, null, ElementFit.Width, gpu);

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Single().Region, Is.EqualTo(new Rect(0, 0, 120, 80)));
            Assert.That(session.Images, Is.Empty, "the surface itself is not put down");
            Assert.That(session.PlacedImages, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ThePictures_AreMadeOnceALayoutAndResolution_AndFreedWithTheDocument()
    {
        var card = Card(out _);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var gpu = new SnapshotRecorder();
        var document = new ElementPrintDocument(card, null, ElementFit.Width, gpu);

        document.Paginate(Sheet);
        await document.PrepareAsync(Sheet, CancellationToken.None);
        await document.PrepareAsync(Sheet, CancellationToken.None);
        var first = gpu.Given[0];
        await PrintAsync(document);
        await PrintAsync(document, new PrintPage(Sheet.Paper) { RasterDotsPerInch = 96 });

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Select(asked => asked.Scale), Is.EqualTo(new[] { 2.0, 2.0, 1.0 }),
                "made once for a layout; laid out again, made again - the element may have changed - and so at another resolution");
            Assert.That(first.IsDisposed, Is.True, "the pictures made again over are freed");
        });

        document.Dispose();
        Assert.That(gpu.Given.All(image => image.IsDisposed), Is.True);
    }

    [Test]
    public async Task APicture_IsNoFinerThanTheSheetShowsTheElement()
    {
        var card = Card(out _);
        card.Width = 720;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(card, null, ElementFit.Width, gpu);

        await PrintAsync(document);

        Assert.That(gpu.Asked.Single().Scale, Is.EqualTo(1).Within(1e-9),
            "192 dpi, but the card shrunk to half its width: one pixel to its DIP is all the paper shows");
    }

    [Test]
    public async Task APicture_IsAsFineAsTheSheetAndWhatHoldsItEnlargeIt()
    {
        var card = Card(out _);
        var lens = new Border { Child = card, RenderTransform = new Transform { ScaleX = 3, ScaleY = 3 } };
        var page = new Border { Child = lens };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(page);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(page, null, ElementFit.None, gpu);
        var sheets = new PrintSheets(document,
            new PrintSettings { RasterDotsPerInch = 150, Scaling = PrintScaling.Custom, ScalePercent = 200 },
            PrinterCapabilities.Pdf);

        sheets.Paginate(sheets.Sheet);
        await sheets.PrepareAsync(sheets.Sheet, CancellationToken.None);

        Assert.That(gpu.Asked.Single().Scale, Is.EqualTo(150 / 96.0 * 2 * 3).Within(1e-6),
            "150 dpi, the sheet at 200%, the card enlarged 3 times inside the element");
    }

    [Test]
    public async Task APictureMadeOnlyInPart_IsNone_AndItsElementIsPrintedFromItsShapes()
    {
        var wide = new Border
        {
            Width = 5000, Height = 100, Background = new SolidColorBrush(Colors.Red),
            Shadow = new Shadow { BlurRadius = 0, OffsetY = 0 },
        };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(wide);
        var gpu = new SnapshotRecorder { FailsFrom = 2 };
        using var document = new ElementPrintDocument(wide, null, ElementFit.None, gpu);
        var sheet = new PrintPage(PaperSize.A4) { RasterDotsPerInch = 600 };
        var session = new RecordingDrawingSession();

        document.Paginate(sheet);
        await document.PrepareAsync(sheet, CancellationToken.None);
        document.DrawPage(0, session);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked, Has.Count.EqualTo(3), "the third square failed; the fourth was not asked for");
            Assert.That(gpu.Given.All(image => image.IsDisposed), Is.True, "the two made are freed");
            Assert.That(session.PlacedImages, Is.Empty);
            Assert.That(session.Geometries, Has.Count.EqualTo(1),
                "a hole nowhere: the strip from its shape, without the shadow that could not be made whole");
        });
    }

    [Test]
    public async Task AnElementWhosePictureCouldNotBeMade_IsPrintedFromItsShapes()
    {
        var card = Card(out _);
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        using var document = new ElementPrintDocument(card, null, ElementFit.Width, new SnapshotRecorder { Fails = true });

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(session.PlacedImages, Is.Empty);
            Assert.That(session.Geometries, Has.Count.EqualTo(2), "the card and the box, from their shapes");
        });
    }

    [Test]
    public async Task AMaskedShadowedElement_IsOnePictureWhole_SoItsMaskReachesWhatItHoldsOnce()
    {
        var card = Card(out _);
        card.OpacityMask = new SolidColorBrush(new Color(0, 0, 0, 128));
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(card);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(card, null, ElementFit.Width, gpu);

        var session = await PrintAsync(document);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked.Single().Content, Is.EqualTo(SnapshotContent.Subtree));
            Assert.That(session.Groups.Where(group => group.Mask != null), Is.Empty, "the mask is in the picture");
            Assert.That(session.Geometries, Is.Empty);
        });
    }

    [Test]
    public async Task ALargePicture_IsMadeInSquaresTheGpuCanDraw()
    {
        var wide = new Border
        {
            Width = 5000, Height = 100, Background = new SolidColorBrush(Colors.Red),
            Shadow = new Shadow { BlurRadius = 0, OffsetY = 0 },
        };
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(wide);
        var gpu = new SnapshotRecorder();
        using var document = new ElementPrintDocument(wide, null, ElementFit.None, gpu);
        var sheet = new PrintPage(PaperSize.A4) { RasterDotsPerInch = 600 };

        document.Paginate(sheet);
        await document.PrepareAsync(sheet, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(gpu.Asked, Has.Count.EqualTo(4), "5000 at 6.25 pixels to the DIP is four squares of 8192");
            Assert.That(gpu.Asked.Max(asked => asked.Region.Width * asked.Scale), Is.LessThanOrEqualTo(8192));
            Assert.That(gpu.Asked.Sum(asked => asked.Region.Width), Is.EqualTo(5000).Within(1e-6));
        });
    }

    [Test]
    public void APrintersRaster_IsItsResolution_NoFinerThanWorthMaking_AndAFilesTheOneChosen()
    {
        var file = new PrintSheets(new ElementPrintDocument(new Border()), new PrintSettings { RasterDotsPerInch = 150 },
            PrinterCapabilities.Pdf);
        var printer = new Printer("Laser", "Laser");
        var fine = new PrintSheets(new ElementPrintDocument(new Border()),
            new PrintSettings { Printer = printer, Quality = new PrintQuality(1200, 1200) }, PrinterCapabilities.Pdf);
        var draft = new PrintSheets(new ElementPrintDocument(new Border()),
            new PrintSettings { Printer = printer, Quality = new PrintQuality(300, 300) }, PrinterCapabilities.Pdf);

        Assert.Multiple(() =>
        {
            Assert.That(file.Sheet.RasterDotsPerInch, Is.EqualTo(150));
            Assert.That(fine.Sheet.RasterDotsPerInch, Is.EqualTo(PrintSettings.FinestPrinterRaster));
            Assert.That(draft.Sheet.RasterDotsPerInch, Is.EqualTo(300));
        });
    }
}
