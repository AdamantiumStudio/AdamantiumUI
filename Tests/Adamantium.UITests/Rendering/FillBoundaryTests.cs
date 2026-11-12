using System.Linq;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media.Fill;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

public class FillBoundaryTests
{
    // Far from the origin a float steps by about 0.001, coarser than the coordinate step: the edge keeps the mesh's own points.
    [Test]
    public void AnEdgeFarFromTheOrigin_KeepsTheMeshPointsExactly()
    {
        const double x = 10000.1234;
        const double y = 20000.5678;
        var mesh = new Mesh().SetPoints(new Vector2<double>[]
        {
            new(x, y), new(x + 1, y), new(x + 1, y + 1),
            new(x, y), new(x + 1, y + 1), new(x, y + 1),
        });

        var loops = FillBoundary.ExtractLoops(mesh);
        var corners = mesh.Points.Select(p => new Vector2<double>(p.X, p.Y)).Distinct().ToArray();

        Assert.That(loops, Has.Count.EqualTo(1));
        Assert.That(loops[0].Points, Is.EquivalentTo(corners));
    }

    // Copies of one corner apart by floating-point noise are one corner, so the shared side stays inside.
    [Test]
    public void CornersApartByNoise_AreOneCorner()
    {
        const double noise = 1e-9;
        var mesh = new Mesh().SetPoints(new Vector2<double>[]
        {
            new(0, 0), new(10, 0), new(10, 10),
            new(noise, -noise), new(10 - noise, 10 + noise), new(0, 10),
        });

        var loops = FillBoundary.ExtractLoops(mesh);

        Assert.That(loops, Has.Count.EqualTo(1));
        Assert.That(loops[0].Points, Has.Length.EqualTo(4));
    }

    // A side is one side whichever end it is given from, a corner with no number in it included.
    [Test]
    public void ASide_IsTheSameEitherWayRound()
    {
        var a = new Vector2<double>(double.NaN, 0);
        var b = new Vector2<double>(1, 0);

        Assert.That(new TriangleSide(a, b), Is.EqualTo(new TriangleSide(b, a)));
    }
}
