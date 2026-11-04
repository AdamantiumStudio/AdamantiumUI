using System;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering.RenderUnits;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A stroke's AA feather is about a device pixel wide. Under a perspective a pixel is not as wide everywhere, and the
// top-left 2x2 of the projective matrix is no scale at all: leaned far enough, it passes through zero and the feather
// it gave spread across the whole stroke - a leaned map blurred around one angle and sharp again past it.
[TestFixture]
public class StrokeFringeUnderPerspectiveTests
{
    private const double Height = 1000;

    // A surface leaned as a map leans it: about its middle, with its eye 0.62 heights away.
    private static Matrix4x4<float> Leaned(double pitch)
    {
        var m = new Transform
        {
            RotationX = pitch,
            Perspective = 0.62 * Height,
            RotationCenterX = Height / 2,
            RotationCenterY = Height / 2,
        }.Matrix;
        return new Matrix4x4<float>((float)m.M11, (float)m.M12, (float)m.M13, (float)m.M14,
            (float)m.M21, (float)m.M22, (float)m.M23, (float)m.M24,
            (float)m.M31, (float)m.M32, (float)m.M33, (float)m.M34,
            (float)m.M41, (float)m.M42, (float)m.M43, (float)m.M44);
    }

    private static Vector2<double> Projected(Matrix4x4<float> t, double x, double y)
    {
        var w = x * t.M14 + y * t.M24 + t.M44;
        return new Vector2<double>((x * t.M11 + y * t.M21 + t.M41) / w, (x * t.M12 + y * t.M22 + t.M42) / w);
    }

    // The area scale by finite differences of the projected points themselves.
    private static double Measured(Matrix4x4<float> t, double x, double y)
    {
        const double step = 0.01;
        var here = Projected(t, x, y);
        var right = Projected(t, x + step, y) - here;
        var down = Projected(t, x, y + step) - here;
        return Math.Sqrt(Math.Abs(right.X * down.Y - right.Y * down.X)) / step;
    }

    [TestCase(0, 500, 500)]
    [TestCase(30, 200, 100)]
    [TestCase(47, 500, 500)]
    [TestCase(47, 900, 50)]
    [TestCase(60, 100, 900)]
    public void TheScale_IsTheProjectionsOwn_AtThePoint(double pitch, double x, double y)
    {
        var t = Leaned(pitch);

        Assert.That(GpuStrokeRenderComponent.ProjectedScaleAt(t, (float)x, (float)y),
            Is.EqualTo(Measured(t, x, y)).Within(1e-3 * Measured(t, x, y) + 1e-6));
    }

    [Test]
    public void AtTheLeanWhereTheTopLeft2x2Vanishes_TheScaleDoesNot()
    {
        var pitch = Math.Atan(2 * 0.62) * 180 / Math.PI;
        var t = Leaned(pitch);
        var topLeft = Math.Sqrt(Math.Abs(t.M11 * t.M22 - t.M12 * t.M21));

        Assert.Multiple(() =>
        {
            Assert.That(topLeft, Is.LessThan(0.01), "the 2x2 the feather was taken from is all but zero here");
            Assert.That(GpuStrokeRenderComponent.ProjectedScaleAt(t, 500, 500), Is.GreaterThan(0.3),
                "the projection itself still draws the middle of the surface at a fair scale");
        });
    }
}
