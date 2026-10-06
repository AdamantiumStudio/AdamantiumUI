using Adamantium.Mathematics;
using Adamantium.UI.Controls.Shapes;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A bezier line is made and holds its kind and control points. Its properties were registered with the value
/// type and the owner swapped, so making one threw.</summary>
[TestFixture]
public class BezierLinePropertiesTests
{
    [Test]
    public void ABezierLine_IsMade_AndKeepsItsKindAndControlPoints()
    {
        var line = new BezierLine
        {
            BezierType = BezierType.Cubic,
            ControlPoint1 = new Vector2(10, 20),
            ControlPoint2 = new Vector2(30, 40)
        };

        Assert.Multiple(() =>
        {
            Assert.That(new BezierLine().BezierType, Is.EqualTo(BezierType.Quadratic));
            Assert.That(line.BezierType, Is.EqualTo(BezierType.Cubic));
            Assert.That(line.ControlPoint1, Is.EqualTo(new Vector2(10, 20)));
            Assert.That(line.ControlPoint2, Is.EqualTo(new Vector2(30, 40)));
        });
    }
}
