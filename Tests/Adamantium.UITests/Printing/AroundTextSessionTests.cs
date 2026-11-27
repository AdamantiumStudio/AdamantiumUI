using System.Linq;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Printing;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Printing;

/// <summary>An element's drawing in two goes around its text: the first passes on what comes before its first text and
/// each text's background, the second its text - without the background - and what comes after, a strikethrough over
/// the letters.</summary>
public class AroundTextSessionTests
{
    private static void Draw(IDrawingSession session)
    {
        session.DrawRectangle(new SolidColorBrush(Colors.Gray), new Rect(0, 0, 100, 20), new ProceduralGeometry.CornerRadius(0));
        session.DrawText(new TextRenderingParameters(), new Size<double>(100, 20), null, new SolidColorBrush(Colors.Black),
            new SolidColorBrush(Colors.Yellow), null, Matrix4x4F.Identity);
        session.DrawRectangle(new SolidColorBrush(Colors.Red), new Rect(0, 9, 100, 2), new ProceduralGeometry.CornerRadius(0));
    }

    [Test]
    public void AFurtherText_KeepsItsBackground_InTheSecondGo()
    {
        var page = new RecordingDrawingSession();
        var placed = new UI.Core.Printing.TransformedDrawingSession(page, Matrix4x4F.Identity);
        void Twice(IDrawingSession session)
        {
            Draw(session);
            session.DrawText(new TextRenderingParameters(), new Size<double>(100, 20), null,
                new SolidColorBrush(Colors.Black), new SolidColorBrush(Colors.Green), null, Matrix4x4F.Identity);
        }

        Twice(new AroundTextSession(placed, false));
        page.Order.Add("|");
        Twice(new AroundTextSession(placed, true));

        Assert.Multiple(() =>
        {
            Assert.That(page.Order, Is.EqualTo(new[] { "shape", "shape", "|", "text", "shape", "text" }),
                "the second text's background goes down with it, after the strikethrough, as drawn");
            Assert.That(((SolidColorBrush)page.TextBackgrounds[1]).Color, Is.EqualTo(Colors.Green));
        });
    }

    [Test]
    public void TheFirstGo_IsWhatComesBeforeTheText_AndItsBackground_TheSecond_TheTextAndWhatComesAfter()
    {
        var page = new RecordingDrawingSession();
        var placed = new UI.Core.Printing.TransformedDrawingSession(page, Matrix4x4F.Identity);

        Draw(new AroundTextSession(placed, false));
        page.Order.Add("|");
        Draw(new AroundTextSession(placed, true));

        Assert.Multiple(() =>
        {
            Assert.That(page.Order, Is.EqualTo(new[] { "shape", "shape", "|", "text", "shape" }),
                "the fill and the text's background; then the text and the strikethrough over it");
            Assert.That(((SolidColorBrush)page.Geometries[1].Brush).Color, Is.EqualTo(Colors.Yellow));
            Assert.That(page.TextBackgrounds.Single(), Is.Null, "the background was laid in the first go");
            Assert.That(((SolidColorBrush)page.Geometries[2].Brush).Color, Is.EqualTo(Colors.Red));
        });
    }
}
