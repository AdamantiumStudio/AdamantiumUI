using System.Linq;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Curves;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>A geometry that is its curves alone: its bounds are its curves' ink, its curves for drawing are the curves it
/// was made of, and its figures, mesh and area are those curves flattened finely, filled by their rule.</summary>
public class CurveGeometryTests
{
    // A square with a rounded side: lines and a quadratic curve bulging to x = 125.
    private static PathCurves Shape()
    {
        var builder = new PathCurveBuilder();
        builder.BeginFigure(new Vector2<double>(0, 0), true);
        builder.LineTo(new Vector2<double>(100, 0));
        builder.QuadraticTo(new Vector2<double>(150, 50), new Vector2<double>(100, 100));
        builder.LineTo(new Vector2<double>(0, 100));
        builder.EndFigure(true);
        return builder.Build(0, null, FillRule.NonZero);
    }

    [Test]
    public void ItsBounds_AreItsInk_AndItsCurvesForDrawingTheCurvesItWasMadeOf()
    {
        var curves = Shape();
        var geometry = new CurveGeometry(curves);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.Bounds.Right, Is.EqualTo(125).Within(1e-9), "the bounds reach the control point, not the ink");
            Assert.That(geometry.Bounds.Width, Is.EqualTo(125).Within(1e-9));
            Assert.That(geometry.CurvesForDrawing.Curves, Is.EqualTo(curves.Curves));
        });
    }

    [Test]
    public void AClone_KeepsItsCurvesAndItsTransform()
    {
        var geometry = new CurveGeometry(Shape()) { Transform = new Transform { RotationAngle = 10 } };

        var clone = geometry.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(clone.Transform, Is.SameAs(geometry.Transform));
            Assert.That(clone.Bounds, Is.EqualTo(geometry.Bounds));
        });
    }

    [Test]
    public void ItsMesh_IsCutFromItsCurves_FlattenedFinely()
    {
        var geometry = new CurveGeometry(Shape());

        geometry.ProcessGeometry(GeometryType.Both);

        Assert.Multiple(() =>
        {
            Assert.That(geometry.Mesh.Points.Length, Is.GreaterThanOrEqualTo(3), "no triangles cut");
            Assert.That(geometry.GetFigures().Single().Points.Length, Is.GreaterThan(20),
                "the bulge is a few straight pieces, faceted at any zoom");
        });
    }

    [Test]
    public void ItsArea_IsFilledByItsCurvesRule()
    {
        var (rings, rule) = new CurveGeometry(Shape()).Area();

        Assert.Multiple(() =>
        {
            Assert.That(rule, Is.EqualTo(FillRule.NonZero), "glyphs that overlap would leave holes even-odd");
            Assert.That(rings, Has.Count.EqualTo(1));
        });
    }
}
