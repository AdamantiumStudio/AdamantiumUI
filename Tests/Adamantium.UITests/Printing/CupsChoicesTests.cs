using System.Collections.Generic;
using System.Linq;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Platforms.Cups;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>A CUPS printer's IPP answers read into what the dialog offers, and the dialog's choices written as a job's
/// attributes - the answers as a cups-pdf queue and an IPP Everywhere printer give them.</summary>
public class CupsChoicesTests
{
    private static readonly CupsMedia A4 = new("iso_a4_210x297mm", "A4", 20990, 29704, 340, 340, 340, 340);

    // As libcups lists them: by size, the borderless first.
    private static readonly CupsAnswers Answers = new()
    {
        Media =
        [
            new CupsMedia("na_letter_8.5x11in_borderless", "US Letter Borderless", 21590, 27940, 0, 0, 0, 0),
            new CupsMedia("na_letter_8.5x11in", "US Letter", 21590, 27940, 635, 102, 635, 1168),
            new CupsMedia("iso_a4_210x297mm", "A4", 21000, 29700, 0, 0, 0, 0),
            A4,
            new CupsMedia("na_number-10_4.125x9.5in", "#10 Envelope", 10477, 24130, 0, 0, 0, 0),
        ],
        DefaultMedia = A4,
        DefaultSource = "auto",
        Sides = ["one-sided", "two-sided-long-edge", "two-sided-short-edge"],
        ColorModes = ["auto", "color", "monochrome"],
        Resolutions = [new PrintQuality(300, 300), new PrintQuality(600, 1200)],
        DefaultResolution = new PrintQuality(300, 300),
        Sources = [new CupsValue("main", "Main"), new CupsValue("auto", "Automatic")],
        Types =
        [
            new CupsValue("stationery", "Plain Paper"), new CupsValue("labels", "Labels"),
            new CupsValue("stationery-letterhead", "stationery-letterhead"),
        ],
        MaxCopies = 999,
        Handling = ["separate-documents-uncollated-copies", "separate-documents-collated-copies"],
    };

    [Test]
    public void WhatThePrinterOffers_IsWhatTheDialogShows()
    {
        var offered = new CupsChoices(Answers).Capabilities;

        Assert.Multiple(() =>
        {
            Assert.That(offered.Papers.Select(paper => paper.Name), Is.EqualTo(new[] { "A4", "Letter", "#10 Envelope" }));
            Assert.That(offered.DefaultPaper, Is.SameAs(PaperSize.A4), "a driver's rounding is still A4");
            Assert.That(offered.MarginsOf(PaperSize.A4).Left, Is.EqualTo(340 * 96 / 2540.0).Within(1e-9), "its own margins");
            Assert.That(offered.Duplex, Is.EqualTo(new[] { DuplexMode.OneSided, DuplexMode.LongEdge, DuplexMode.ShortEdge }));
            Assert.That(offered.Colors, Is.EqualTo(new[] { PrintColorMode.Color, PrintColorMode.Grayscale }));
            Assert.That(offered.Qualities, Is.EqualTo(Answers.Resolutions));
            Assert.That(offered.Trays, Is.EqualTo(new[] { "Automatic", "Main" }), "the printer's own choice first");
            Assert.That(offered.MediaTypes, Is.EqualTo(new[] { "Auto", "Plain Paper", "Labels", "Stationery letterhead" }),
                "left to the printer, which says no default; one it does not name, in words");
            Assert.That(offered.MarginsOf(PaperSize.Letter).Bottom, Is.EqualTo(1168 * 96 / 2540.0).Within(1e-9),
                "not borderless");
            Assert.That(offered.MaxCopies, Is.EqualTo(999));
            Assert.That(offered.Collates, Is.True);
            Assert.That(offered.HasDriverSettings, Is.False);
        });
    }

    [Test]
    public void APlainPrinter_OffersNothingMore()
    {
        var choices = new CupsChoices(new CupsAnswers { ColorModes = ["monochrome"], Sides = ["one-sided"] });
        var offered = choices.Capabilities;

        Assert.Multiple(() =>
        {
            Assert.That(offered.Duplex, Is.EqualTo(new[] { DuplexMode.OneSided }));
            Assert.That(offered.Colors, Is.EqualTo(new[] { PrintColorMode.Grayscale }));
            Assert.That(offered.Qualities, Is.Empty);
            Assert.That(offered.Trays, Is.Empty);
            Assert.That(offered.Papers, Is.EqualTo(PaperSize.Standard));
        });
    }

    [Test]
    public void TheChoices_AreTheJobsAttributes()
    {
        var choices = new CupsChoices(Answers);
        var written = choices.Write(new PrintSettings
        {
            Paper = PaperSize.A4, Duplex = DuplexMode.ShortEdge, Color = PrintColorMode.Grayscale, Copies = 3,
            Collate = false, Quality = new PrintQuality(600, 1200), Tray = "Main", MediaType = "Labels",
        }).ToDictionary(option => option.Key, option => option.Value);

        Assert.That(written, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["media-col"] = "{media-size={x-dimension=20990 y-dimension=29704} media-left-margin=340 media-top-margin=340 " +
                            "media-right-margin=340 media-bottom-margin=340 media-source=main media-type=labels}",
            ["sides"] = "two-sided-short-edge",
            ["print-color-mode"] = "monochrome",
            ["printer-resolution"] = "600x1200dpi",
            ["copies"] = "3",
            ["multiple-document-handling"] = "separate-documents-uncollated-copies",
            ["print-scaling"] = "none",
        }));
    }

    [Test]
    public void APaperAlone_IsNamed_AndNothingElseIsAsked()
    {
        var choices = new CupsChoices(new CupsAnswers { Media = Answers.Media });

        Assert.That(choices.Write(new PrintSettings { Paper = PaperSize.Letter, Copies = 1 }), Is.EqualTo(new[]
        {
            new KeyValuePair<string, string>("media", "na_letter_8.5x11in"),
            new KeyValuePair<string, string>("print-scaling", "none"),
        }));
    }

    [Test]
    public void WhatTheSystemPutRight_IsReadBack()
    {
        var choices = new CupsChoices(Answers);

        var right = choices.ReadBack(new Dictionary<string, string>
        {
            ["media"] = "na_letter_8.5x11in", ["sides"] = "one-sided", ["print-color-mode"] = "monochrome",
            ["printer-resolution"] = "300dpi", ["copies"] = "2", ["something-else"] = "x",
        }, new PrintSettings { Duplex = DuplexMode.LongEdge, Quality = new PrintQuality(600, 1200) });

        Assert.Multiple(() =>
        {
            Assert.That(right.Paper, Is.SameAs(PaperSize.Letter));
            Assert.That(right.Duplex, Is.EqualTo(DuplexMode.OneSided));
            Assert.That(right.Color, Is.EqualTo(PrintColorMode.Grayscale));
            Assert.That(right.Quality, Is.EqualTo(new PrintQuality(300, 300)));
            Assert.That(right.Copies, Is.EqualTo(2));
        });
    }

    [TestCase("na_number-10_4.125x9.5in", "Number 10")]
    [TestCase("iso_sra4_225x320mm_borderless", "SRA4")]
    [TestCase("iso_dl_110x220mm", "DL")]
    [TestCase("jis_b5_182x257mm", "JIS B5")]
    [TestCase("custom_148.52x209.9mm_148.52x209.9mm_borderless", "148.52 × 209.9 mm")]
    [TestCase("strange", "strange")]
    public void APaperNotNamed_IsNamedFromItsKeyword(string keyword, string name)
    {
        var choices = new CupsChoices(new CupsAnswers { Media = [new CupsMedia(keyword, null, 12345, 23456, 0, 0, 0, 0)] });

        Assert.That(choices.Capabilities.Papers.Single().Name, Is.EqualTo(name));
    }

    [Test]
    public void TheDefaultPaper_UnderANameOfItsOwn_IsFoundByItsSize() =>
        Assert.That(CupsChoices.NearestTo(Answers.Media, new CupsMedia("another_name", null, 21000, 29700, 0, 0, 0, 0)),
            Is.SameAs(A4));

    [Test]
    public void WhatIsLeftAsThePrinterHasIt_IsNotAskedFor()
    {
        var choices = new CupsChoices(Answers);
        var settings = new PrintSettings().FitTo(choices.Capabilities);

        Assert.That(choices.Write(settings), Has.Member(new KeyValuePair<string, string>("media", "iso_a4_210x297mm")));
        Assert.That(choices.Write(settings).Select(option => option.Key), Has.No.Member("media-col"));
    }

    [Test]
    public void ToCheck_ThePapersSourceAndType_AreOnTheirOwn()
    {
        var choices = new CupsChoices(Answers);
        var settings = new PrintSettings { Paper = PaperSize.A4, Tray = "Main", MediaType = "Labels" };

        var checking = choices.WriteToCheck(settings).ToDictionary(option => option.Key, option => option.Value);
        var right = choices.ReadBack(new Dictionary<string, string> { ["media-type"] = "stationery", ["media-source"] = "auto" },
            settings);

        Assert.Multiple(() =>
        {
            Assert.That(checking["media"], Is.EqualTo("iso_a4_210x297mm"));
            Assert.That(checking["media-source"], Is.EqualTo("main"));
            Assert.That(checking["media-type"], Is.EqualTo("labels"));
            Assert.That(checking.Keys, Has.No.Member("media-col"));
            Assert.That(right.MediaType, Is.EqualTo("Plain Paper"));
            Assert.That(right.Tray, Is.EqualTo("Automatic"));
        });
    }

    [Test]
    public void ATypeNamedInWords_IsQuoted()
    {
        var choices = new CupsChoices(new CupsAnswers
        {
            Media = Answers.Media, Types = [new CupsValue("Company \"Head\" {x}", null), new CupsValue("stationery", null)],
        });

        var written = choices.Write(new PrintSettings { Paper = PaperSize.A4, MediaType = "Company \"Head\" {x}" });

        Assert.That(written[0].Value, Does.EndWith(" media-type=\"Company \\\"Head\\\" {x}\"}"));
    }

    [TestCase("3", "none", PrinterState.Ready)]
    [TestCase("4", "none", PrinterState.Printing)]
    [TestCase("5", "paused", PrinterState.Paused)]
    [TestCase("3", "media-empty-error", PrinterState.OutOfPaper)]
    [TestCase("3", "media-low-warning,toner-low-report", PrinterState.Ready)]
    [TestCase("4", "media-jam", PrinterState.PaperJam)]
    [TestCase("5", "offline-report, paused", PrinterState.Offline)]
    [TestCase("4", "connecting-to-device", PrinterState.Printing)]
    [TestCase("5", "connecting-to-device,media-empty", PrinterState.Offline)]
    [TestCase("3", "cover-open-error", PrinterState.Error)]
    [TestCase(null, null, PrinterState.Ready)]
    public void TheState_IsWhatHoldsThePrinterUp(string state, string reasons, PrinterState expected) =>
        Assert.That(CupsChoices.StateOf(state, reasons), Is.EqualTo(expected));
}
