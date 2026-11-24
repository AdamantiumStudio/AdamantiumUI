using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Adamantium.Imaging;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Objects;
using Adamantium.Pdf.Resources;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Printing;
using Adamantium.Vulkan.Core;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Pens PDF cannot stroke in one go - trimmed, dashed, their ends unlike each other or PDF's - written as the
/// stroke shader lays them; and tile brushes, laid out as the renderer lays them, once or as tiling patterns.</summary>
public class PdfPensAndTilesTests
{
    private static readonly Color Red = new(255, 0, 0, 255);
    private static readonly Color Blue = new(0, 0, 255, 255);

    private sealed class OnePage(Action<IDrawingSession> draw) : IPrintDocument
    {
        public string Title => "Page";

        public int Paginate(PrintPage page) => 1;

        public void DrawPage(int index, IDrawingSession session) => draw(session);
    }

    private static byte[] Bytes(Action<IDrawingSession> draw)
    {
        using var output = new MemoryStream();
        PdfPrintFile.Save(new OnePage(draw), new PrintPage(PaperSize.A4), output);
        return output.ToArray();
    }

    private static PdfDocument Save(Action<IDrawingSession> draw) => PdfDocument.Open(Bytes(draw));

    private static string Content(PdfDocument document) =>
        Encoding.ASCII.GetString(document.Pages[0].GetContents().Span);

    private static PdfResources Resources(PdfDocument document) => new(document.Pages[0].Resources, document);

    private static RecordingSession Drawn(PdfDocument document)
    {
        var page = PdfPageDrawing.Build(document, document.Pages[0]);
        var session = new RecordingSession();
        page.Render(session, Matrix4x4<float>.Identity, null);
        session.UnwrapPage();
        return session;
    }

    /// <summary>The left and right of what <paramref name="drawn"/> covers, in the sheet's points.</summary>
    private static (double Left, double Right) Across(DrawnGeometry drawn)
    {
        drawn.Geometry.RecalculateBounds();
        var box = drawn.Geometry.Bounds;
        var (one, _) = PdfPageDrawingTests.At(drawn.Transform, box.X, box.Y);
        var (two, _) = PdfPageDrawingTests.At(drawn.Transform, box.Right, box.Bottom);
        return (Math.Min(one, two), Math.Max(one, two));
    }

    private static BitmapSource Picture(int width, int height)
    {
        var pixels = Enumerable.Repeat((byte)200, width * height * 4).ToArray();
        return new BitmapSource((uint)width, (uint)height, 1, 1, (SurfaceFormat)Format.R8G8B8A8_UNORM, pixels);
    }

    // The trim is a share of the contour's length: the middle half of a line is its middle half.
    [Test]
    public void ATrim_DrawsItsShareOfTheLine()
    {
        var saved = Save(session => session.DrawLine(new Vector2<double>(0, 100), new Vector2<double>(400, 100),
            new Pen(new SolidColorBrush(Red), 4, trimStart: 0.25, trimEnd: 0.75)));

        var (left, right) = Across(Drawn(saved).All<DrawnGeometry>().Single());
        Assert.That(left, Is.EqualTo(75).Within(1e-3), "a quarter of 400 DIPs in, in points");
        Assert.That(right, Is.EqualTo(225).Within(1e-3));
    }

    [Test]
    public void AnEmptyTrim_DrawsNothing()
    {
        var saved = Save(session => session.DrawLine(new Vector2<double>(0, 100), new Vector2<double>(400, 100),
            new Pen(new SolidColorBrush(Red), 4, trimStart: 0.6, trimEnd: 0.6)));

        Assert.That(Drawn(saved).All<DrawnGeometry>(), Is.Empty);
    }

    // PDF has one cap for both ends: a flat start and a pointed end are a line without caps and the point as a shape.
    [Test]
    public void EndsUnlikeEachOther_AreTheLineAndTheirShapes()
    {
        var saved = Save(session => session.DrawLine(new Vector2<double>(0, 100), new Vector2<double>(400, 100),
            new Pen(new SolidColorBrush(Red), 20, startLineCap: PenLineCap.Flat,
                endLineCap: PenLineCap.ConvexTriangle)));

        var drawn = Drawn(saved).All<DrawnGeometry>().ToList();
        Assert.That(drawn, Has.Count.EqualTo(2), "the line, then its pointed end");
        Assert.That(drawn[0].Pen, Is.Not.Null);
        Assert.That(drawn[1].Brush, Is.Not.Null);
        Assert.That(Across(drawn[0]).Right, Is.EqualTo(300).Within(1e-3), "the line stops at its end");
        Assert.That(Across(drawn[1]).Right, Is.EqualTo(307.5).Within(1e-3), "the point reaches half the width past it");
    }

    // A concave end is bitten out of its line: the bite is cut away before the line is stroked.
    [Test]
    public void AConcaveEnd_IsBittenOutOfItsLine()
    {
        var saved = Save(session => session.DrawLine(new Vector2<double>(0, 100), new Vector2<double>(400, 100),
            new Pen(new SolidColorBrush(Red), 20, startLineCap: PenLineCap.ConcaveRound,
                endLineCap: PenLineCap.Flat)));

        var content = Content(saved);
        Assert.That(content.IndexOf("W* n", StringComparison.Ordinal), Is.GreaterThan(0));
        Assert.That(content.IndexOf("W* n", StringComparison.Ordinal),
            Is.LessThan(content.IndexOf("\nS\n", StringComparison.Ordinal)), "cut before it is stroked");
    }

    // Dashes are runs along the line, each ending in the dash caps - round by default - and the line's own at its ends.
    [Test]
    public void Dashes_AreRunsAlongTheLine()
    {
        var saved = Save(session => session.DrawLine(new Vector2<double>(0, 100), new Vector2<double>(100, 100),
            new Pen(new SolidColorBrush(Red), 2, dashStrokeArray: [20, 10])));

        var content = Content(saved);
        Assert.That(Regex.Matches(content, @" m\n").Count, Is.EqualTo(6),
            "0-20, 30-50, 60-80, 90-100, and the round ends of the first and last drawn as shapes");
        Assert.That(content, Does.Contain("1 J"), "round dash caps at both ends of the middle two, PDF's own");
        Assert.That(content, Does.Contain("0 J"), "the first and the last start and end flat, as the line does");
    }

    // Fitted to a closed contour, the dashes go round a whole number of times: no long dash at the seam.
    [Test]
    public void DashesFittedToAClosedContour_GoRoundAWholeNumberOfTimes()
    {
        var saved = Save(session => session.DrawRectangle(null, new Rect(0, 0, 50, 50),
            new Pen(new SolidColorBrush(Red), 2, dashStrokeArray: [30, 15], fitDashesToContour: true,
                dashStartCap: PenLineCap.Flat, dashEndCap: PenLineCap.Flat)));

        Assert.That(Regex.Matches(Content(saved), @" m\n").Count, Is.EqualTo(4), "200 DIPs round, 45 a period: 4");
    }

    // Measured in the window's units, the line is as wide on the sheet whatever its drawing is scaled by.
    [Test]
    public void APenInTheWindowsUnits_IsAsWideOnTheSheetAtAnyScale()
    {
        var saved = Save(session => session.DrawGeometry(null, new RectangleGeometry(new Rect(0, 0, 10, 10)),
            new Pen(new SolidColorBrush(Red), 8) { ThicknessUnit = PenThicknessUnit.Window },
            Matrix4x4.CreateScale<float>(4, 4, 1)));

        Assert.That(Content(saved), Does.Contain("\n2 w\n"), "8 DIPs on the sheet, 2 in the drawing scaled by 4");
    }

    [Test]
    public void ARepeatedPicture_IsATilingPattern_OfOneViewport()
    {
        var brush = new ImageBrush(Picture(4, 4))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 40, 30),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        var saved = Save(session => session.DrawRectangle(brush, new Rect(0, 0, 200, 120)));

        var pattern = (PdfStream)Resources(saved).Pattern("P0");
        Assert.Multiple(() =>
        {
            Assert.That(((PdfInteger)pattern.Dictionary["PatternType"]).Value, Is.EqualTo(1));
            Assert.That(Number(pattern.Dictionary["XStep"]), Is.EqualTo(40).Within(1e-9));
            Assert.That(Number(pattern.Dictionary["YStep"]), Is.EqualTo(30).Within(1e-9));
            Assert.That(Content(saved), Does.Contain("/Pattern cs /P0 scn"));
        });
    }

    // Flipped, a cell holds the tile and its mirror: twice as wide, so the copies meet edge to edge mirrored.
    [Test]
    public void AFlippedTile_IsACellOfItAndItsMirror()
    {
        var brush = new ImageBrush(Picture(4, 4))
        {
            TileMode = TileMode.FlipX,
            Viewport = new Rect(0, 0, 0.25, 0.5),
        };
        var saved = Save(session => session.DrawRectangle(brush, new Rect(0, 0, 200, 120)));

        var pattern = (PdfStream)Resources(saved).Pattern("P0");
        Assert.That(Number(pattern.Dictionary["XStep"]), Is.EqualTo(100).Within(1e-9));
        Assert.That(Number(pattern.Dictionary["YStep"]), Is.EqualTo(60).Within(1e-9));
    }

    private static double Number(PdfObject value) => value switch
    {
        PdfInteger integer => integer.Value,
        PdfReal real => real.Value,
        _ => double.NaN,
    };

    // One copy is drawn once into the shape, cut to it - not a pattern.
    [Test]
    public void APictureNotRepeated_IsDrawnOnceIntoItsShape()
    {
        var saved = Save(session => session.DrawEllipse(new Rect(0, 0, 160, 120),
            new ImageBrush(Picture(4, 4)) { Stretch = Stretch.Uniform }, null, null));

        Assert.That(Resources(saved).Dictionary.ContainsKey("Pattern"), Is.False);
        Assert.That(Content(saved), Does.Contain("W n").And.Contain("/Fm0 Do"));
    }

    // A drawing brush's tile is the drawing itself, in vector: no image anywhere.
    [Test]
    public void ADrawingsTile_StaysVector()
    {
        var drawing = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Red),
            Geometry = new EllipseGeometry(new Rect(0, 0, 10, 10)),
        };
        var saved = Save(session => session.DrawRectangle(
            new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 0.25, 0.25) },
            new Rect(0, 0, 200, 120)));

        var pattern = (PdfStream)Resources(saved).Pattern("P0");
        var cell = new PdfResources((PdfDictionary)saved.Resolve(pattern.Dictionary["Resources"]), saved);
        var form = cell.XObject("Fm0");
        var inner = (PdfDictionary)saved.Resolve(form.Dictionary["Resources"]);
        Assert.That(inner?.ContainsKey("XObject") ?? false, Is.False, "the cell draws the circle as a path");
        Assert.That(Encoding.ASCII.GetString(saved.Decode(form).Data.Span), Does.Contain(" c\n"));
    }

    // As the shader does: brightness, contrast and saturation first, then the tint multiplied in - alpha too.
    [Test]
    public void APicturesTintAndAdjustment_ChangeItsPixelsAsTheRendererDoes()
    {
        byte[] white = [255, 255, 255, 255];
        PdfPictures.Recolor(white, new Color(255, 0, 0, 128), default);
        Assert.That(white, Is.EqualTo(new byte[] { 255, 0, 0, 128 }));

        byte[] gray = [128, 128, 128, 255];
        PdfPictures.Recolor(gray, new Color(255, 255, 255, 255), new Vector4<float>(0.25f, 0, 0, 0));
        Assert.That(gray[0], Is.EqualTo((byte)Math.Round((128 / 255.0 + 0.25) * 255)), "brightness added");

        byte[] red = [255, 0, 0, 255];
        PdfPictures.Recolor(red, new Color(255, 255, 255, 255), new Vector4<float>(0, 0, -1, 0));
        Assert.That(red[0], Is.EqualTo(red[1]).And.EqualTo(red[2]), "no saturation leaves the gray");
    }

    [Test]
    public void ATintedPictureTile_IsAnImageOfTheTintedPixels()
    {
        var brush = new ImageBrush(Picture(2, 2)) { Tint = new Color(255, 0, 0, 255) };
        var saved = Save(session => session.DrawRectangle(brush, new Rect(0, 0, 100, 100)));

        var form = Resources(saved).XObject("Fm0");
        var image = FirstImage(saved, form);
        var pixels = Adamantium.Pdf.Images.PdfImageDecoder.Decode(image,
            new PdfResources(new PdfDictionary(), saved), saved).Rgba;
        Assert.That(pixels.Take(4), Is.EqualTo(new byte[] { 200, 0, 0, 200 }), "200 gray times red; its alpha too");
    }

    // A drawing's tint is multiplied over it, let through only where the drawing is - by its own alpha.
    [Test]
    public void ATintedDrawingTile_IsMultipliedOverIt_WhereItIs()
    {
        var drawing = new GeometryDrawing
        {
            Brush = new SolidColorBrush(Red),
            Geometry = new EllipseGeometry(new Rect(0, 0, 10, 10)),
        };
        var text = Encoding.ASCII.GetString(Bytes(session => session.DrawRectangle(
            new DrawingBrush(drawing) { Tint = Blue }, new Rect(0, 0, 100, 100))));
        Assert.That(text, Does.Contain("/Multiply"));
        Assert.That(text, Does.Contain("/S /Alpha").Or.Contain("/S/Alpha"));
    }

    /// <summary>The first image reached from <paramref name="form"/>, through the forms it draws.</summary>
    private static PdfStream FirstImage(PdfDocument document, PdfStream form)
    {
        var resources = (PdfDictionary)document.Resolve(form.Dictionary["Resources"]);
        if (resources?["XObject"] is not PdfDictionary objects)
        {
            return null;
        }

        foreach (var name in objects.Keys)
        {
            var inner = (PdfStream)document.Resolve(objects[name]);
            if (inner.Dictionary["Subtype"] is PdfName { Value: "Image" })
            {
                return inner;
            }

            if (FirstImage(document, inner) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    [Test]
    public void ATileStroke_IsAPattern()
    {
        var brush = new ImageBrush(Picture(4, 4))
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 16, 16),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        var saved = Save(session => session.DrawRectangle(null, new Rect(10, 10, 100, 80), new Pen(brush, 10)));

        Assert.That(Content(saved), Does.Contain("/Pattern CS /P0 SCN"));
    }
}
