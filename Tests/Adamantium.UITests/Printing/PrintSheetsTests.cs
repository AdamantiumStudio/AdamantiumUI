using System;
using System.Collections.Generic;
using System.Linq;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>The print dialog's own options, as arithmetic: which pages, the printer's choices fitted to what it can do,
/// and pages laid out on sheets - scaled, placed, several to a sheet.</summary>
public class PrintSheetsTests
{
    /// <summary>Pages that each draw one rectangle of their whole size - where it lands is where the page went.</summary>
    private sealed class Pages(int count) : IPrintDocument
    {
        public PrintPage LaidOutOn { get; private set; }

        public string Title => "Pages";

        public int Paginate(PrintPage page)
        {
            LaidOutOn = page;
            return count;
        }

        public void DrawPage(int index, IDrawingSession session)
        {
            var size = LaidOutOn.Size;
            session.DrawGeometry(new SolidColorBrush(new Color((byte)index, (byte)0, (byte)0, (byte)255)),
                new RectangleGeometry(new Rect(0, 0, size.Width, size.Height)), null);
        }
    }

    /// <summary>Where page rectangle <paramref name="drawn"/> lands on the sheet.</summary>
    private static Rect Landed((Brush Brush, Geometry Geometry, Pen Pen, Matrix4x4F Transform) drawn)
    {
        var box = ((RectangleGeometry)drawn.Geometry).Rect;
        var m = drawn.Transform;
        var left = box.X * m.M11 + m.M41;
        var top = box.Y * m.M22 + m.M42;
        return new Rect(left, top, box.Width * m.M11, box.Height * m.M22);
    }

    [TestCase("1-3, 5", 6, new[] { 0, 1, 2, 4 })]
    [TestCase("8-", 10, new[] { 7, 8, 9 })]
    [TestCase("-2", 5, new[] { 0, 1 })]
    [TestCase("5, 1, 5", 6, new[] { 0, 4 })]
    [TestCase("3-30", 4, new[] { 2, 3 })]
    public void ChosenPages_AreTheOnesWrittenOut_InOrder_OncEach(string text, int count, int[] expected)
    {
        Assert.That(PageRange.TryParse(text, out var range), Is.True);
        Assert.That(range.Pages(count, 0), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("0")]
    [TestCase("4-2")]
    [TestCase("a")]
    [TestCase("1,,x")]
    public void WhatIsNotPageNumbers_IsNoRange(string text) =>
        Assert.That(PageRange.TryParse(text, out _), Is.False);

    [Test]
    public void TheCurrentPage_IsTheOneShown_KeptInTheDocument()
    {
        Assert.That(PageRange.Current.Pages(5, 3), Is.EqualTo(new[] { 3 }));
        Assert.That(PageRange.Current.Pages(5, 9), Is.EqualTo(new[] { 4 }));
        Assert.That(PageRange.All.Pages(3, 0), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    // A choice the printer does not offer goes back to its default; what it offers is kept.
    [Test]
    public void SettingsFittedToAPrinter_KeepOnlyWhatItOffers()
    {
        var capabilities = new PrinterCapabilities
        {
            Papers = [PaperSize.Letter, PaperSize.Legal],
            DefaultPaper = PaperSize.Letter,
            MaxCopies = 99,
            Duplex = [DuplexMode.OneSided, DuplexMode.LongEdge],
            Colors = [PrintColorMode.Grayscale],
            Qualities = [new PrintQuality(300, 300), new PrintQuality(600, 600)],
            DefaultQuality = new PrintQuality(600, 600),
            Trays = ["Auto", "Manual"],
        };
        var asked = new PrintSettings
        {
            Paper = PaperSize.A4,
            Copies = 500,
            Duplex = DuplexMode.ShortEdge,
            Color = PrintColorMode.Color,
            Quality = new PrintQuality(1200, 1200),
            Tray = "Manual",
            PagesPerSheet = 3,
            ScalePercent = 1000,
        };

        var fitted = asked.FitTo(capabilities);

        Assert.Multiple(() =>
        {
            Assert.That(fitted.Paper, Is.EqualTo(PaperSize.Letter), "no A4: the printer's own default");
            Assert.That(fitted.Copies, Is.EqualTo(99));
            Assert.That(fitted.Duplex, Is.EqualTo(DuplexMode.OneSided));
            Assert.That(fitted.Color, Is.EqualTo(PrintColorMode.Grayscale));
            Assert.That(fitted.Quality, Is.EqualTo(new PrintQuality(600, 600)));
            Assert.That(fitted.Tray, Is.EqualTo("Manual"), "offered, so kept");
            Assert.That(fitted.PagesPerSheet, Is.EqualTo(1));
            Assert.That(fitted.ScalePercent, Is.EqualTo(400));
            Assert.That(fitted.Collate, Is.False, "it does not collate");
        });
    }

    [Test]
    public void APage_FitsTheSheetInsideThePrintersMargins_Centred()
    {
        var capabilities = new PrinterCapabilities
        {
            Margins = new Dictionary<string, Thickness> { [PaperSize.A4.Name] = new Thickness(20) },
        };
        var document = new Pages(1);
        var sheets = new PrintSheets(document, new PrintSettings { Paper = PaperSize.A4 }, capabilities);

        Assert.That(sheets.Paginate(sheets.Sheet), Is.EqualTo(1));
        var session = new RecordingDrawingSession();
        sheets.DrawPage(0, session);

        var landed = Landed(session.Geometries.Single());
        var scale = Math.Min((PaperSize.A4.Width - 40) / PaperSize.A4.Width, (PaperSize.A4.Height - 40) / PaperSize.A4.Height);
        Assert.That(landed.Width, Is.EqualTo(PaperSize.A4.Width * scale).Within(1e-2));
        Assert.That(landed.X, Is.EqualTo(20).Within(1e-2), "A4's width is what fits between the margins first");
        Assert.That(landed.Y + landed.Height / 2, Is.EqualTo(PaperSize.A4.Height / 2).Within(1e-2), "centred down");
        Assert.That(session.Groups.Single().Clip, Is.InstanceOf<RectangleGeometry>(), "cut to what the printer reaches");
    }

    [Test]
    public void APageAtItsOwnSize_KeepsItsPlaceOnTheSheet()
    {
        var sheets = new PrintSheets(new Pages(1), new PrintSettings { Scaling = PrintScaling.ActualSize },
            PrinterCapabilities.Pdf);
        sheets.Paginate(sheets.Sheet);
        var session = new RecordingDrawingSession();
        sheets.DrawPage(0, session);

        Assert.That(Landed(session.Geometries.Single()), Is.EqualTo(new Rect(0, 0, PaperSize.A4.Width,
            PaperSize.A4.Height)));
    }

    // Four pages to a portrait sheet: two across, two down, each a quarter of the sheet, in reading order.
    [Test]
    public void FourPagesToASheet_AreLaidTwoByTwo_InReadingOrder()
    {
        var sheets = new PrintSheets(new Pages(6), new PrintSettings { PagesPerSheet = 4 }, PrinterCapabilities.Pdf);

        Assert.That(sheets.Paginate(sheets.Sheet), Is.EqualTo(2), "six pages fill two sheets");
        var session = new RecordingDrawingSession();
        sheets.DrawPage(0, session);
        var landed = session.Geometries.Select(Landed).ToList();
        Assert.That(landed, Has.Count.EqualTo(4));
        Assert.That(landed[0].Width, Is.EqualTo(PaperSize.A4.Width / 2).Within(1e-2));
        Assert.That(landed[1].X, Is.GreaterThan(landed[0].X), "the second to the right of the first");
        Assert.That(landed[2].Y, Is.GreaterThan(landed[0].Y), "the third below the first");

        var second = new RecordingDrawingSession();
        sheets.DrawPage(1, second);
        Assert.That(second.Geometries, Has.Count.EqualTo(2), "pages 5 and 6");
    }

    // Two portrait pages on a portrait sheet are larger one above the other than side by side.
    [Test]
    public void TwoPagesToASheet_TakeTheWayThatShowsThemLargest()
    {
        var sheets = new PrintSheets(new Pages(2), new PrintSettings { PagesPerSheet = 2 }, PrinterCapabilities.Pdf);
        sheets.Paginate(sheets.Sheet);
        var session = new RecordingDrawingSession();
        sheets.DrawPage(0, session);

        var landed = session.Geometries.Select(Landed).ToList();
        Assert.That(landed[1].Y, Is.GreaterThan(landed[0].Y));
        Assert.That(landed[1].X, Is.EqualTo(landed[0].X).Within(1e-2));
    }

    [Test]
    public void TheRange_PicksThePagesPrinted_AndTheDocumentIsLaidOutOnTheSheet()
    {
        PageRange.TryParse("2, 4", out var range);
        var document = new Pages(5);
        var sheets = new PrintSheets(document, new PrintSettings
        {
            Range = range,
            Paper = PaperSize.Letter,
            Orientation = PageOrientation.Landscape,
        }, PrinterCapabilities.Pdf);

        Assert.That(sheets.Paginate(sheets.Sheet), Is.EqualTo(2));
        Assert.That(sheets.Pages, Is.EqualTo(new[] { 1, 3 }));
        Assert.That(document.LaidOutOn.Size.Width, Is.EqualTo(PaperSize.Letter.Height), "landscape letter");
        var session = new RecordingDrawingSession();
        sheets.DrawPage(1, session);
        Assert.That(((SolidColorBrush)session.Geometries.Single().Brush).Color.R, Is.EqualTo(3), "the fourth page");
    }

    [Test]
    public void NoPagesChosen_IsOneBlankSheet()
    {
        PageRange.TryParse("9", out var range);
        var sheets = new PrintSheets(new Pages(2), new PrintSettings { Range = range }, PrinterCapabilities.Pdf);

        Assert.That(sheets.Paginate(sheets.Sheet), Is.EqualTo(1));
        var session = new RecordingDrawingSession();
        sheets.DrawPage(0, session);
        Assert.That(session.Geometries, Is.Empty);
    }
}
