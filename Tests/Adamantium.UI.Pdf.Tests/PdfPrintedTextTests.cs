using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Adamantium.Fonts;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.Pdf.Document;
using Adamantium.Pdf.Text;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Printing;
using NUnit.Framework;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Text of a document saved as PDF, read back as text: what it says, where it stands, and what is still drawn by
/// its outlines.</summary>
public class PdfPrintedTextTests
{
    private static readonly SolidColorBrush Black = new(new Color(0, 0, 0, 255));

    private sealed class Page(Action<IDrawingSession> draw) : IPrintDocument
    {
        public string Title => "Text";

        public int Paginate(PrintPage page) => 1;

        public void DrawPage(int index, IDrawingSession session) => draw(session);
    }

    private static Typeface Font() =>
        Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts", "SourceSans3-Bold.ttf"), 3);

    private static TextLayout Lay(string text, double width = double.NaN, TextTrimming trimming = TextTrimming.None,
        double size = 20)
    {
        var font = Font();
        var layout = new TextLayout(font, font.GetFont(0));
        layout.ProcessText(text, size, new Size<double>(width, double.NaN),
            double.IsNaN(width) || trimming != TextTrimming.None ? TextWrapping.NoWrap : TextWrapping.WrapByWords, trimming,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    private static PdfDocument Save(Action<IDrawingSession> draw)
    {
        using var output = new MemoryStream();
        PdfPrintFile.SaveAsync(new Page(draw), new PrintPage(PaperSize.A4), output).GetAwaiter().GetResult();
        if (Environment.GetEnvironmentVariable("ADAM_KEEP_PDF") is { Length: > 0 } folder)
        {
            File.WriteAllBytes(Path.Combine(folder, TestContext.CurrentContext.Test.MethodName + ".pdf"), output.ToArray());
        }

        return PdfDocument.Open(output.ToArray());
    }

    private static PdfDocument Save(TextLayout layout, Brush brush) => Save(session =>
        session.DrawText(new TextRenderingParameters(), default, layout, brush, null, null));

    private static string Text(PdfDocument document) => document.Pages[0].ReadText().Text.Trim();

    [Test]
    public void ATextsBackground_IsLaidUnderIt_AsOnTheScreen()
    {
        var red = new SolidColorBrush(new Color(255, 0, 0, 255));
        var saved = Save(session =>
            session.DrawText(new TextRenderingParameters(), new Size<double>(100, 30), Lay("Back"), Black, red, null));

        var content = System.Text.Encoding.ASCII.GetString(saved.Pages[0].GetContents().Span);
        var fill = content.IndexOf("1 0 0 rg", StringComparison.Ordinal);
        Assert.That(fill, Is.GreaterThanOrEqualTo(0).And.LessThan(content.IndexOf("BT", StringComparison.Ordinal)),
            "the red background, before the text");
    }

    [TestCase("The quick brown fox, 2025")]
    [TestCase("office affluent waffle")]
    [TestCase("a\tb")]
    public void Text_IsReadBack(string text)
    {
        Assert.That(Text(Save(Lay(text), Black)), Is.EqualTo(text.Replace('\t', ' ')));
    }

    [Test]
    public void EachGlyph_StandsWhereItWasLaid()
    {
        var layout = Lay("Where");
        var read = Save(layout, Black).Pages[0].ReadText().Glyphs;

        var laid = layout.GetTextData();
        Assert.That(read.Count, Is.EqualTo(laid.Length));
        for (var index = 0; index < laid.Length; index++)
        {
            Assert.That(read[index].Origin.X, Is.EqualTo((laid[index].PenX + laid[index].OffsetX) * 0.75).Within(0.05));
        }
    }

    [Test]
    public void WrappedLines_KeepTheirWords()
    {
        var text = Text(Save(Lay("The first words of a staff and the last of it", 140), Black));

        Assert.That(string.Join(' ', text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)),
            Is.EqualTo("The first words of a staff and the last of it"));
    }

    [Test]
    public void WhatAnEllipsisCutsOff_IsNotSaid()
    {
        var text = Text(Save(Lay("The beginning and a hidden end", 120, TextTrimming.CharEllipses), Black));

        Assert.That(text, Does.StartWith("The b").And.EndWith("..."));
        Assert.That(text, Does.Not.Contain("hidden"));
    }

    [Test]
    public void WhatIsCutOffWithoutAnEllipsis_IsNotSaid()
    {
        var font = Font();
        var layout = new TextLayout(font, font.GetFont(0));
        layout.ProcessText("Visible andhidden100000", 20, new Size<double>(90, double.NaN), TextWrapping.NoWrap,
            TextTrimming.None, HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        var laid = layout.GetTextData().Length;

        Assert.That(laid, Is.LessThan(layout.Text.Length));
        Assert.That(Text(Save(layout, Black)), Is.EqualTo(layout.Text[..laid].Trim()));
    }

    [Test]
    public void ALineEndingInALigature_KeepsItsLetters()
    {
        Assert.That(Text(Save(Lay("the staff" + (char)0x2028 + "and affi"), Black)).Split((char[])null, StringSplitOptions.RemoveEmptyEntries),
            Is.EqualTo(new[] { "the", "staff", "and", "affi" }));
    }

    [Test]
    public void PlacedGlyphs_AreSaidByTheirFont()
    {
        var font = Font().GetFont(0);
        var glyphs = "Run".Select((letter, at) => new PositionedGlyph(font.GetGlyphByUnicode(letter).Index, 10 + at * 14, 30))
            .ToArray();

        Assert.That(Text(Save(session => session.DrawGlyphs(new GlyphRun(font, 20, glyphs), Black, null,
            Matrix4x4<float>.Identity))), Is.EqualTo("Run"));
    }

    [Test]
    public void AGradientsText_IsDrawnByItsOutlines()
    {
        var gradient = new LinearGradientBrush([new GradientStop(new Color(255, 0, 0, 255), 0),
            new GradientStop(new Color(0, 0, 255, 255), 1)]);
        var saved = Save(Lay("Gradient"), gradient);

        Assert.That(Text(saved), Is.Empty);
        Assert.That(saved.Pages[0].Resources.ContainsKey("Shading"), Is.True);
        Assert.That(saved.Pages[0].Resources.ContainsKey("Font"), Is.False);
    }

    // The saved page read back and drawn: the glyph runs it draws, each with its fill, its stroke and its matrix.
    private static List<DrawnGlyphs> Drawn(TextLayout layout)
    {
        var document = Save(layout, Black);
        var page = PdfPageDrawing.Build(document, document.Pages[0]);
        var session = new RecordingSession();
        page.Render(session, Matrix4x4<float>.Identity, null);
        return session.Calls.OfType<DrawnGlyphs>().ToList();
    }

    private static TextLayout Lay(AttributedText text, WritingMode mode = WritingMode.Horizontal)
    {
        var font = Font();
        var layout = new TextLayout(font, font.GetFont(0)) { WritingMode = mode };
        layout.ProcessText(text, 20, new Size<double>(double.NaN, double.NaN), TextWrapping.NoWrap, TextTrimming.None,
            HorizontalTextAlignment.Left, VerticalTextAlignment.Top);
        return layout;
    }

    // Whether a matrix turns or slants what it draws: its first two columns are not upright and square to each other.
    private static bool Slants(Matrix4x4<float> m) => Math.Abs(m.M11 * m.M21 + m.M12 * m.M22) > 1e-3 * Math.Abs(m.M11 * m.M22);

    private static bool Turns(Matrix4x4<float> m) => Math.Abs(m.M12) > Math.Abs(m.M11);

    [Test]
    public void AFragmentsOwnColor_IsTheColorOfItsGlyphs()
    {
        var red = new Color(255, 0, 0, 255);
        var drawn = Drawn(Lay(new AttributedText("black red").Apply(6, 3, new TextAttributes { Foreground = red })));

        var fills = drawn.Select(run => ((SolidColorBrush)run.Fill).Color).ToList();
        Assert.That(fills, Does.Contain(red), "the red fragment is printed black");
        Assert.That(fills, Does.Contain(Black.Color), "the rest is not black");
    }

    [Test]
    public void TheSpacesOfAColoredFragment_StayInItsText_UnderAGradient()
    {
        var gradient = new LinearGradientBrush([new GradientStop(new Color(255, 0, 0, 255), 0),
            new GradientStop(new Color(0, 0, 255, 255), 1)]);
        var layout = Lay(new AttributedText("plain red words here").Apply(6, 14,
            new TextAttributes { Foreground = new Color(255, 0, 0, 255) }));

        Assert.That(Text(Save(layout, gradient)), Is.EqualTo("red words here"));
    }

    [Test]
    public void AHalfSeeThroughSynthesizedBold_IsOneInk_FadedAsAGroup()
    {
        var drawn = Drawn(Lay(new AttributedText("faded").Apply(0, 5, new TextAttributes
        {
            Foreground = new Color(255, 0, 0, 128),
            Synthesis = FontSynthesis.Weight,
        })));

        Assert.That(drawn, Is.Not.Empty, "the faded bold text is not text");
        Assert.Multiple(() =>
        {
            Assert.That(drawn.All(run => ((SolidColorBrush)run.Fill).Color.A == 255 && run.Depth > 0), Is.True,
                "the fill and the thickening are laid one over the other at half each");
        });
    }

    [Test]
    public void AHalfSeeThroughGradientsSynthesizedBold_IsOneInk_FadedAsAGroup()
    {
        var gradient = new LinearGradientBrush([new GradientStop(new Color(255, 0, 0, 255), 0),
            new GradientStop(new Color(0, 0, 255, 255), 1)]) { Opacity = 0.5 };
        var saved = Save(Lay(new AttributedText("faded").Apply(0, 5,
            new TextAttributes { Synthesis = FontSynthesis.Weight })), gradient);
        var page = PdfPageDrawing.Build(saved, saved.Pages[0]);
        var session = new RecordingSession();
        page.Render(session, Matrix4x4<float>.Identity, null);
        var drawn = session.Calls.OfType<DrawnGeometry>().Where(call => call.Brush is GradientBrush || call.Pen != null)
            .ToList();

        Assert.That(drawn, Is.Not.Empty, "the bold is not drawn by its outlines in the gradient");
        Assert.That(drawn.All(call => call.Depth > 0 && (call.Brush ?? call.Pen.Brush).Opacity >= 1), Is.True,
            "the fill and the thickening are laid one over the other at half each");
    }

    [Test]
    public void ASeeThroughSynthesizedBold_IsNotWritten_AsASeeThroughPlainFaceIsNot()
    {
        var layout = Lay(new AttributedText("shown unseen").Apply(6, 6, new TextAttributes
        {
            Foreground = new Color(255, 0, 0, 0),
            Synthesis = FontSynthesis.Weight,
        }));

        Assert.That(Text(Save(layout, Black)), Is.EqualTo("shown"));
    }

    [Test]
    public void ASynthesizedBold_IsDrawnThicker()
    {
        var drawn = Drawn(Lay(new AttributedText("bold").Apply(0, 4,
            new TextAttributes { Synthesis = FontSynthesis.Weight })));

        Assert.That(drawn, Is.Not.Empty, "the bold text is not text");
        Assert.That(drawn.All(run => run.Stroke is { Thickness: > 0 }), Is.True, "the synthesized bold is not thickened");
    }

    [Test]
    public void ASynthesizedItalic_IsSlanted()
    {
        var drawn = Drawn(Lay(new AttributedText("slant").Apply(0, 5,
            new TextAttributes { Synthesis = FontSynthesis.Style })));

        Assert.That(drawn, Is.Not.Empty, "the italic text is not text");
        Assert.That(drawn.All(run => Slants(run.Transform)), Is.True, "the synthesized italic stands upright");
    }

    [Test]
    public void LatinInAVerticalLine_IsTurnedOnItsSide()
    {
        var drawn = Drawn(Lay(new AttributedText("Side"), WritingMode.VerticalRightToLeft));

        Assert.That(drawn, Is.Not.Empty, "the turned text is not text");
        Assert.That(drawn.All(run => Turns(run.Transform)), Is.True, "the letters of a vertical line are not turned");
    }

    [Test]
    public void AFadedGroupsText_IsText()
    {
        var saved = Save(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
            session.DrawText(new TextRenderingParameters(), default, Lay("Faded"), Black, null, null);
            session.PopGroup();
        });

        Assert.That(Text(saved), Is.EqualTo("Faded"));
    }
}
