using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using Adamantium.UI.Core.Printing;
using Adamantium.UI.Platforms.Windows.Printing;
using Adamantium.Win32;
using Adamantium.Win32.Printing;
using Adamantium.Win32.Printing.Xps;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>A sheet written as XPS through the object model, as a print job writes it - here into a package of its own,
/// whose page is read back as markup.</summary>
[Platform("Win")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class XpsDrawingSessionTests
{
    private static readonly XNamespace Xps = "http://schemas.microsoft.com/xps/2005/06";

    /// <summary>The probe document written as an XPS package; its first page's markup and the package's parts.</summary>
    private static (XElement Page, string[] Parts) Written(IPrintDocument document = null)
    {
        using var zip = new ZipArchive(new MemoryStream(Package(document)), ZipArchiveMode.Read);
        var page = zip.Entries.Single(entry => entry.FullName.EndsWith(".fpage"));
        using var reader = page.Open();
        return (XDocument.Load(reader).Root, zip.Entries.Select(entry => entry.FullName).ToArray());
    }

    /// <summary>The fonts of the package, each read back from its obfuscated part (XPS 1.0, 9.1.7.3): the first 32 bytes
    /// XORed with the GUID of its part's name, its bytes taken from the end.</summary>
    private static List<byte[]> EmbeddedFonts(IPrintDocument document)
    {
        using var zip = new ZipArchive(new MemoryStream(Package(document)), ZipArchiveMode.Read);
        var fonts = new List<byte[]>();
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".odttf")))
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var data = copy.ToArray();
            var guid = Path.GetFileNameWithoutExtension(entry.FullName).Replace("-", "");
            var key = Enumerable.Range(0, 16).Select(at => Convert.ToByte(guid.Substring(at * 2, 2), 16)).ToArray();
            for (var at = 0; at < 32; at++)
            {
                data[at] ^= key[15 - at % 16];
            }

            fonts.Add(data);
        }

        return fonts;
    }

    private static byte[] Package(IPrintDocument document)
    {
        var factoryClass = PrintDocumentPackage.XpsObjectFactoryClass;
        var factoryInterface = typeof(IXpsOMObjectFactory).GUID;
        Marshal.ThrowExceptionForHR(Win32Interop.CoCreateInstance(ref factoryClass, IntPtr.Zero,
            Win32Interop.ClsCtxInprocServer, ref factoryInterface, out var created));
        var factory = (IXpsOMObjectFactory)created;
        using var package = new MemoryStream();
        try
        {
            Marshal.ThrowExceptionForHR(factory.CreatePartUri("/FixedDocumentSequence.fdseq", out var sequence));
            Marshal.ThrowExceptionForHR(factory.CreatePackageWriterOnStream(new ComStream(package), 0,
                XpsInterleaving.Off, sequence, IntPtr.Zero, IntPtr.Zero, null, IntPtr.Zero, out var writer));
            var sheets = new PrintSheets(document ?? new XpsProbeDocument(), new PrintSettings(), PrinterCapabilities.Pdf);
            sheets.Paginate(sheets.Sheet);
            XpsPrintJob.WriteAsync(factory, writer, sheets, sheets.Sheet, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            Marshal.ReleaseComObject(writer);
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        return package.ToArray();
    }

    private static XElement[] Paths(XElement page) => page.Descendants(Xps + "Path").ToArray();

    [Test]
    public void APenXpsStrokesAsItIs_IsItsOwn_WithItsDashesInWidths()
    {
        var dashed = Paths(Written().Page).Single(path => path.Attribute("StrokeDashArray") != null &&
                                                          path.Attribute("StrokeThickness")?.Value == "4");

        Assert.Multiple(() =>
        {
            Assert.That(dashed.Attribute("StrokeDashArray")!.Value, Is.EqualTo("3 1.5"), "12 and 6 by a width of 4");
            Assert.That(dashed.Attribute("StrokeDashCap")!.Value, Is.EqualTo("Round"));
        });
    }

    [Test]
    public void APenWithATrim_IsStrokedAsTheShaderLaysIt()
    {
        var trimmed = Paths(Written().Page).Single(path => path.Attribute("StrokeThickness")?.Value == "10");

        Assert.That(trimmed.Attribute("StrokeDashArray"), Is.Null, "no dashes: the run itself");
        Assert.That(trimmed.Attribute("Data")!.Value, Does.Contain("120,220").And.Contains("360,220"),
            "the fifth to the four fifths of a line from 40 to 440");
    }

    [Test]
    public void AFadedGroupCutToACircle_IsACanvas()
    {
        var page = Written().Page;

        Assert.That(page.Descendants(Xps + "Canvas").Any(canvas =>
            canvas.Attribute("Opacity")?.Value == "0.5" && canvas.Attribute("Clip") != null));
    }

    /// <summary>A page of one rectangle stroked with dashes round it - laid out in runs - by a gradient of one stop, half
    /// see-through, the brush at half.</summary>
    private sealed class OneStop : IPrintDocument
    {
        public string Title => "One stop";

        public int Paginate(PrintPage page) => 1;

        public void DrawPage(int index, UI.Core.Graphics.IDrawingSession session) =>
            session.DrawRectangle(null, new UI.Core.Rect(10, 10, 100, 50), new UI.Core.Media.Pen(
                new UI.Core.Media.LinearGradientBrush(
                    [new UI.Core.Media.GradientStop(new Mathematics.Color((byte)200, (byte)100, (byte)50, (byte)128), 0.5)])
                {
                    Opacity = 0.5,
                }, 4, dashStrokeArray: [10, 5]));
    }

    // A pen in runs is drawn opaque in a canvas faded by the brush's opacity: the stop's own alpha stays in its color.
    [Test]
    public void AGradientOfOneStop_IsItsColor_ItsOwnAlphaKept()
    {
        var page = Written(new OneStop()).Page;
        var faded = page.Descendants(Xps + "Canvas").Single(canvas => canvas.Attribute("Opacity")?.Value == "0.5");

        Assert.That(faded.Descendants(Xps + "Path").Select(path => path.Attribute("Stroke")?.Value),
            Has.All.EqualTo("#80c86432").IgnoreCase);
    }

    [Test]
    public void APicture_IsAnImageOfThePackage_AndAReflectedGradient_IsWrittenOut()
    {
        var (page, parts) = Written();

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Some.EndsWith(".png"));
            Assert.That(page.Descendants(Xps + "ImageBrush").Count(), Is.EqualTo(1));
            var linear = page.Descendants(Xps + "LinearGradientBrush").Single();
            Assert.That(linear.Attribute("SpreadMethod")?.Value ?? "Pad", Is.EqualTo("Pad"),
                "repeats written out, not left to the reader");
            Assert.That(linear.Descendants(Xps + "GradientStop").Count(), Is.GreaterThanOrEqualTo(8),
                "a quarter of the box a period: four of them, two stops each");
        });
    }

    [Test]
    public void AFragmentsColorAndSynthesizedBold_AreItsOwnPath()
    {
        var page = Written(new FragmentsPrintDocument()).Page;
        var paths = Paths(page);
        var glyphs = page.Descendants(Xps + "Glyphs").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(glyphs.Any(run => run.Attribute("Fill")?.Value is { } fill &&
                fill.EndsWith("000000", StringComparison.OrdinalIgnoreCase)),
                Is.True, "the black words are not glyphs filled black");
            Assert.That(paths.Any(path => path.Attribute("Fill")?.Value is { } fill &&
                fill.EndsWith("ff0000", StringComparison.OrdinalIgnoreCase) &&
                path.Attribute("Stroke")?.Value is { } stroke && stroke.EndsWith("ff0000", StringComparison.OrdinalIgnoreCase)),
                Is.True, "the red bold word is not filled and thickened in red");
        });
    }

    // The black words set as glyphs embed their font: a subset of the glyphs the page drew, read back as OpenType.
    [Test]
    public void GlyphsSetOnAPage_EmbedTheirFont_AsASubsetOfWhatThePageDrew()
    {
        var page = Written(new FragmentsPrintDocument()).Page;
        var run = page.Descendants(Xps + "Glyphs").Single();
        var fonts = EmbeddedFonts(new FragmentsPrintDocument());

        Assert.Multiple(() =>
        {
            Assert.That(run.Attribute("FontUri")?.Value, Does.EndWith(".odttf"));
            Assert.That(run.Attribute("Indices")?.Value, Is.Not.Empty);
            Assert.That(fonts, Has.Count.EqualTo(1), "one font for the page");
            var read = Adamantium.Fonts.Typeface.LoadFont(fonts[0], 3);
            Assert.That(read.GlyphCount, Is.EqualTo(6), "glyph 0 and the five letters of 'black'");
            Assert.That(read.GetFont(0).UnitsPerEm, Is.EqualTo(1000));

            // The run's indices are the subset's: each stands for its letter in the font embedded.
            var said = string.Concat(run.Attribute("Indices")!.Value.Split(';').Select(entry =>
                read.GetFont(0).GetGlyphText(uint.Parse(entry.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture))
                    .FirstOrDefault() ?? "?"));
            Assert.That(said, Is.EqualTo("black"), "the indices are not the subset's");
        });
    }
}
