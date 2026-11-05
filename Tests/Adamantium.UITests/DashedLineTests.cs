using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A dotted or dashed line of text drawn along its band: across it for a line under text, down it for a border
/// at a paragraph's side.</summary>
[TestFixture]
public class DashedLineTests
{
    [Test]
    public void ALineDownItsBand_RunsDownTheMiddle_AsThickAsTheBandIsWide()
    {
        var session = new RecordingDrawingSession();
        DashedLine.Draw(session, new Rect(10, 20, 2, 100), new SolidColorBrush(Colors.Red), [2, 2], true);
        var (start, end, pen) = session.Lines.Single();

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(new Vector2<double>(11, 20)));
            Assert.That(end, Is.EqualTo(new Vector2<double>(11, 120)));
            Assert.That(pen.Thickness, Is.EqualTo(2));
            Assert.That(pen.DashStrokeArray, Is.EqualTo(new double[] { 2, 2 }));
        });
    }

    [Test]
    public void ALineAcrossItsBand_RunsAlongTheMiddle()
    {
        var session = new RecordingDrawingSession();
        DashedLine.Draw(session, new Rect(10, 20, 100, 3), new SolidColorBrush(Colors.Red), [6, 4], false);
        var (start, end, pen) = session.Lines.Single();

        Assert.Multiple(() =>
        {
            Assert.That(start, Is.EqualTo(new Vector2<double>(10, 21.5)));
            Assert.That(end, Is.EqualTo(new Vector2<double>(110, 21.5)));
            Assert.That(pen.Thickness, Is.EqualTo(3));
        });
    }

    [Test]
    public void ABorderTallerThanWide_RunsDown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DashedLine.RunsDown(new RectangleF(0, 0, 2, 50)), Is.True);
            Assert.That(DashedLine.RunsDown(new RectangleF(0, 0, 50, 2)), Is.False);
        });
    }
}
