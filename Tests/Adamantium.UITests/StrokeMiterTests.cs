using System.Linq;
using Adamantium.Core.TypeParsing;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A mitred corner reaches no farther than four half-thicknesses from the corner, as the GPU stroke draws it, so
/// a line turning nearly back on itself - common in map data - has no spike without end.</summary>
[TestFixture]
public class StrokeMiterTests
{
    [Test]
    public void ALineTurningNearlyBack_HasAMiterOfLimitedReach()
    {
        var line = TypeParser.Parse<Geometry>("M0,0 L100,0 L0,1");
        line.ProcessGeometry(GeometryType.Both);
        var stroke = new StrokeGeometry(new Pen(Brushes.Black, 10), line);

        stroke.ProcessGeometry(GeometryType.Both);

        Assert.That(stroke.Mesh.Points, Is.Not.Empty);
        var reach = stroke.Mesh.Points.Max(p => p.X);
        Assert.That(reach, Is.LessThanOrEqualTo(100 + StrokeGeometry.MiterReach * 10 / 2 + 1e-6));
    }

    [Test]
    public void AGentleCorner_KeepsItsWholeMiter()
    {
        var tip = StrokeGeometry.MiterTip(new Vector2<double>(0, 0), new Vector2<double>(3, 4), 10);

        Assert.That(tip, Is.EqualTo(new Vector2<double>(3, 4)));
    }
}
