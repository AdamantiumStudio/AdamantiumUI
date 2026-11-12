using System.Linq;
using Adamantium.Graphics.Core.Models;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.ProceduralGeometry.Shapes;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using NUnit.Framework;

namespace Adamantium.UITests;

// CombinedGeometry's modes (Union / Intersect / Exclude / Xor) on the CPU, through the engine's overlay; no GPU.
public class CombinedGeometryTests
{
    // ---- disjoint inputs (bounding boxes don't overlap) -> the fixed else-branch ----

    [Test]
    public void Disjoint_Union_CoversBoth()
    {
        var (a, b) = (new Vector2<double>(20, 20), new Vector2<double>(120, 20));
        var m = Combine(GeometryCombineMode.Union, DisjointA(), DisjointB());
        Assert.IsTrue(Covered(m, a), "G1 covered");
        Assert.IsTrue(Covered(m, b), "G2 covered");
    }

    [Test]
    public void Disjoint_Intersect_IsEmpty()
    {
        var m = Combine(GeometryCombineMode.Intersect, DisjointA(), DisjointB());
        Assert.IsFalse(Covered(m, new Vector2<double>(20, 20)), "G1 not covered (empty intersection)");
        Assert.IsFalse(Covered(m, new Vector2<double>(120, 20)), "G2 not covered (empty intersection)");
    }

    [Test]
    public void Disjoint_Exclude_KeepsGeometry1Only()
    {
        var m = Combine(GeometryCombineMode.Exclude, DisjointA(), DisjointB());
        Assert.IsTrue(Covered(m, new Vector2<double>(20, 20)), "G1 kept");
        Assert.IsFalse(Covered(m, new Vector2<double>(120, 20)), "G2 removed");
    }

    [Test]
    public void Disjoint_Xor_CoversBoth()
    {
        var m = Combine(GeometryCombineMode.Xor, DisjointA(), DisjointB());
        Assert.IsTrue(Covered(m, new Vector2<double>(20, 20)), "G1 covered");
        Assert.IsTrue(Covered(m, new Vector2<double>(120, 20)), "G2 covered");
    }

    // ---- overlapping inputs -> the intersection/merge path ----
    // A = [0,60]x[0,60], B = [40,100]x[40,100]; overlap = [40,60]x[40,60].

    [Test]
    public void Overlap_Union_CoversAOnlyBOnlyAndOverlap()
    {
        var m = Combine(GeometryCombineMode.Union, OverlapA(), OverlapB());
        Assert.IsTrue(Covered(m, new Vector2<double>(10, 10)), "A-only");
        Assert.IsTrue(Covered(m, new Vector2<double>(90, 90)), "B-only");
        Assert.IsTrue(Covered(m, new Vector2<double>(50, 50)), "overlap");
        Assert.IsFalse(Covered(m, new Vector2<double>(150, 150)), "exterior empty");
    }

    [Test]
    public void Overlap_Intersect_CoversOnlyOverlap()
    {
        var m = Combine(GeometryCombineMode.Intersect, OverlapA(), OverlapB());
        Assert.IsTrue(Covered(m, new Vector2<double>(50, 50)), "overlap covered");
        Assert.IsFalse(Covered(m, new Vector2<double>(10, 10)), "A-only not covered");
        Assert.IsFalse(Covered(m, new Vector2<double>(90, 90)), "B-only not covered");
    }

    [Test]
    public void Overlap_Exclude_CoversAMinusB()
    {
        var m = Combine(GeometryCombineMode.Exclude, OverlapA(), OverlapB());
        Assert.IsTrue(Covered(m, new Vector2<double>(10, 10)), "A-only kept");
        Assert.IsFalse(Covered(m, new Vector2<double>(50, 50)), "overlap removed");
        Assert.IsFalse(Covered(m, new Vector2<double>(90, 90)), "B-only not added");
    }

    [Test]
    public void Overlap_Xor_CoversSymmetricDifference()
    {
        var m = Combine(GeometryCombineMode.Xor, OverlapA(), OverlapB());
        Assert.IsTrue(Covered(m, new Vector2<double>(10, 10)), "A-only");
        Assert.IsTrue(Covered(m, new Vector2<double>(90, 90)), "B-only");
        Assert.IsFalse(Covered(m, new Vector2<double>(50, 50)), "overlap excluded");
    }

    // ---- nested inputs (one fully inside the other, no edge crossings) ----
    // This is the Border's geometry: outer [0,100]^2 with inner [30,70]^2 fully inside it.

    [Test]
    public void Nested_Exclude_FormsRing()
    {
        var m = Combine(GeometryCombineMode.Exclude, NestedOuter(), NestedInner());
        Assert.IsTrue(Covered(m, new Vector2<double>(10, 50)), "ring (in outer, outside inner) kept");
        Assert.IsFalse(Covered(m, new Vector2<double>(50, 50)), "center (the hole) removed");
        Assert.IsFalse(Covered(m, new Vector2<double>(150, 150)), "exterior empty");
    }

    [Test]
    public void Nested_Intersect_KeepsInner()
    {
        var m = Combine(GeometryCombineMode.Intersect, NestedOuter(), NestedInner());
        Assert.IsTrue(Covered(m, new Vector2<double>(50, 50)), "inner (in both) kept");
        Assert.IsFalse(Covered(m, new Vector2<double>(10, 50)), "ring (outer only) not in intersection");
    }

    [Test]
    public void Nested_Union_KeepsOuter()
    {
        var m = Combine(GeometryCombineMode.Union, NestedOuter(), NestedInner());
        Assert.IsTrue(Covered(m, new Vector2<double>(50, 50)), "center filled");
        Assert.IsTrue(Covered(m, new Vector2<double>(10, 50)), "ring filled");
        Assert.IsFalse(Covered(m, new Vector2<double>(150, 150)), "exterior empty");
    }

    // Regression: nested ellipses (one fully inside the other) combined into a solid shape must fill cleanly.
    // Before coordinate snapping, floating-point-noise-equal points were treated as distinct and the triangulator
    // cut spurious strips/bands through the disc -> some interior samples were left uncovered.
    [Test]
    public void NestedEllipses_Union_FillsWithoutStrips()
    {
        var outer = new EllipseGeometry(new Vector2<double>(100, 100), 80, 80);
        var inner = new EllipseGeometry(new Vector2<double>(100, 100), 40, 40);
        var m = Combine(GeometryCombineMode.Union, outer, inner);

        var offsets = new[] { (0, 0), (0, -60), (0, 60), (-60, 0), (60, 0), (0, -20), (0, 20), (35, 35), (-35, -35) };
        foreach (var (dx, dy) in offsets)
            Assert.IsTrue(Covered(m, new Vector2<double>(100 + dx, 100 + dy)), $"interior point ({dx},{dy}) must be filled (no strip)");
    }

    // Regression: a sector less the sector inside it - outlines sharing their radial edges. The segment walk hung from
    // 295 degrees on and at 45 and 135 filled the hole and left the band empty. Laid over each other, every sweep gives
    // the band: filled between the arcs within the sweep, empty elsewhere, its outline closed rings.
    [Test, Timeout(120_000)]
    public void ASectorLessTheSectorInsideIt_IsTheBand_AtEverySweep()
    {
        var wrong = new System.Collections.Generic.List<string>();
        foreach (var size in new[] { 22.0, 100.0 })
        {
            for (var sweep = 5; sweep <= 360 && wrong.Count < 10; sweep += 5)
            {
                var outer = new EllipseGeometry(new Rect(0, 0, size, size), 0, sweep, EllipseType.Sector);
                var inner = new EllipseGeometry(new Rect(2, 2, size - 4, size - 4), 0, sweep, EllipseType.Sector);
                var mesh = Combine(GeometryCombineMode.Exclude, outer, inner);
                var r = size / 2;
                var misses = Misses(mesh, size, (x, y) =>
                {
                    var (dx, dy) = (x - r, y - r);
                    var distance = System.Math.Sqrt(dx * dx + dy * dy);
                    var angle = (System.Math.Atan2(dy, dx) * 180 / System.Math.PI + 360) % 360;
                    var margin = size / 30;
                    var angleMargin = margin / System.Math.Max(distance, 1) * 180 / System.Math.PI;
                    if (System.Math.Abs(distance - r) < margin || System.Math.Abs(distance - (r - 2)) < margin ||
                        (sweep < 360 && (angle < angleMargin || System.Math.Abs(angle - sweep) < angleMargin ||
                                         angle > 360 - angleMargin)))
                    {
                        return null;
                    }

                    return distance < r && distance > r - 2 && (sweep >= 360 || angle <= sweep);
                });
                var outline = OutlineFault(mesh);
                if (misses > 0 || outline != null)
                {
                    wrong.Add($"{size}px {sweep}deg: {misses} points wrong{(outline != null ? ", " + outline : "")}");
                }
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    // Squares sharing a whole edge, or one edge lying along part of another's, in every mode.
    [TestCase(GeometryCombineMode.Union, 0, 0, 40, 40, 40, 0, 40, 40)]
    [TestCase(GeometryCombineMode.Intersect, 0, 0, 40, 40, 40, 0, 40, 40)]
    [TestCase(GeometryCombineMode.Exclude, 0, 0, 40, 40, 40, 0, 40, 40)]
    [TestCase(GeometryCombineMode.Xor, 0, 0, 40, 40, 40, 0, 40, 40)]
    [TestCase(GeometryCombineMode.Union, 0, 0, 60, 60, 0, 20, 30, 20)]
    [TestCase(GeometryCombineMode.Intersect, 0, 0, 60, 60, 0, 20, 30, 20)]
    [TestCase(GeometryCombineMode.Exclude, 0, 0, 60, 60, 0, 20, 30, 20)]
    [TestCase(GeometryCombineMode.Xor, 0, 0, 60, 60, 0, 20, 30, 20)]
    [TestCase(GeometryCombineMode.Exclude, 0, 0, 60, 60, 20, 0, 20, 80)]
    [TestCase(GeometryCombineMode.Union, 0, 0, 60, 60, 60, 60, 30, 30)]
    public void RectanglesMeetingAlongAnEdge_AreCombinedExactly(GeometryCombineMode mode, double x1, double y1, double w1,
        double h1, double x2, double y2, double w2, double h2)
    {
        var a = new Rect(x1, y1, w1, h1);
        var b = new Rect(x2, y2, w2, h2);
        var mesh = Combine(mode, new RectangleGeometry(a), new RectangleGeometry(b));
        bool In(Rect r, double x, double y) => x > r.X && x < r.X + r.Width && y > r.Y && y < r.Y + r.Height;
        bool Near(Rect r, double x, double y) =>
            x > r.X - 0.5 && x < r.X + r.Width + 0.5 && y > r.Y - 0.5 && y < r.Y + r.Height + 0.5 &&
            !(x > r.X + 0.5 && x < r.X + r.Width - 0.5 && y > r.Y + 0.5 && y < r.Y + r.Height - 0.5);

        var misses = Misses(mesh, 100, (x, y) =>
        {
            if (Near(a, x, y) || Near(b, x, y))
            {
                return null;
            }

            var (inA, inB) = (In(a, x, y), In(b, x, y));
            return mode switch
            {
                GeometryCombineMode.Union => inA || inB,
                GeometryCombineMode.Intersect => inA && inB,
                GeometryCombineMode.Exclude => inA && !inB,
                _ => inA ^ inB
            };
        });

        Assert.Multiple(() =>
        {
            Assert.That(misses, Is.Zero, "points filled or empty against the mode");
            Assert.That(OutlineFault(mesh), Is.Null);
        });
    }

    // Points of a size x size square, on a 60 x 60 grid, where the mesh covers what expected says not to; null skips.
    static int Misses(Mesh mesh, double size, System.Func<double, double, bool?> expected)
    {
        var misses = 0;
        for (var y = 0.37; y < size; y += size / 60)
        {
            for (var x = 0.37; x < size; x += size / 60)
            {
                if (expected(x, y) is { } inside && Covered(mesh, new Vector2<double>(x, y)) != inside)
                {
                    misses++;
                }
            }
        }

        return misses;
    }

    // What is wrong with the outline: a ring not closed or of fewer than three points, an edge of no length, or an edge
    // drawn twice - a spur, there and back, or one edge in two rings.
    static string OutlineFault(Mesh mesh)
    {
        var edges = new System.Collections.Generic.HashSet<((long, long), (long, long))>();
        foreach (var contour in mesh.Contours)
        {
            if (!contour.IsGeometryClosed)
            {
                return "an open contour";
            }

            if (contour.Points.Length < 3)
            {
                return "a contour of fewer than three points";
            }

            for (var i = 0; i < contour.Points.Length; i++)
            {
                var a = Key(contour.Points[i]);
                var b = Key(contour.Points[(i + 1) % contour.Points.Length]);
                if (a == b)
                {
                    return $"an edge of no length at {a}";
                }

                if (!edges.Add(a.CompareTo(b) < 0 ? (a, b) : (b, a)))
                {
                    return $"the edge {a}-{b} drawn twice";
                }
            }
        }

        return null;
    }

    static (long, long) Key(Vector2<double> p) => ((long)System.Math.Round(p.X * 1e6), (long)System.Math.Round(p.Y * 1e6));

    // ---- every mode against the operands themselves: each point is in the result as the mode says of its being in
    // each operand, by the operand's own rule; points within a margin of any edge are not held to it ----

    static readonly GeometryCombineMode[] Modes =
        [GeometryCombineMode.Union, GeometryCombineMode.Intersect, GeometryCombineMode.Exclude, GeometryCombineMode.Xor];

    [Test]
    public void PathGeometryOperands_AreTheirFigures()
    {
        foreach (var mode in Modes)
        {
            AssertExact(mode, Path(false, Square(0, 0, 60)), Path(false, Square(40, 40, 60)));
        }
    }

    [Test]
    public void ATriangleTouchingAnothersSide_IsCombinedExactly()
    {
        foreach (var mode in Modes)
        {
            AssertExact(mode, Path(false, [V(30, 40), V(40, 60), V(20, 50)]), Path(false, [V(20, 60), V(50, 0), V(60, 40)]));
        }
    }

    [TestCase(1, 1, 1, 0)]
    [TestCase(2, 0, 2, 0)]
    [TestCase(0, 0, 0, 2)]
    [TestCase(0, 0, 0, 1)]
    public void ARoundedFrameOfUnevenSides_IsTheBandBetween(double left, double top, double right, double bottom)
    {
        var outer = new Rect(0, 0, 200, 30);
        var inner = outer.Deflate(new Thickness(left, top, right, bottom));
        AssertExact(GeometryCombineMode.Exclude, new RectangleGeometry(outer, new CornerRadius(8)),
            new RectangleGeometry(inner, new CornerRadius(7)), 0.4);
    }

    [Test]
    public void CirclesAlmostTheSame_OrAlmostTouching_AreCombinedExactly()
    {
        foreach (var mode in Modes)
        {
            AssertExact(mode, new EllipseGeometry(V(100, 100), 50, 50), new EllipseGeometry(V(100.0001, 100), 50, 50), 0.5);
            AssertExact(mode, new EllipseGeometry(V(100, 100), 50, 50), new EllipseGeometry(V(100.8, 100), 50.5, 50.5), 0.5);
        }
    }

    [Test]
    public void SquaresMeetingAtACorner_PartThere()
    {
        foreach (var mode in Modes)
        {
            AssertExact(mode, Path(false, Square(50, 50, 50)), Path(false, Square(0, 0, 50)));
            AssertExact(mode, Path(false, Square(50, 50, 50)), Path(false, Square(50, 0, 50), Square(0, 50, 50)));
        }

        var union = Combine(GeometryCombineMode.Union, Path(false, Square(50, 50, 50)), Path(false, Square(0, 0, 50)));
        Assert.That(union.Contours.Count, Is.EqualTo(2), "two squares, not a figure eight");
    }

    [Test]
    public void AnAreaUnderNonZero_IsFilledWhereItsRingsWind()
    {
        // A frame of two rings running the same way: solid under non-zero.
        var frame = Path(true, Square(0, 0, 100), Square(25, 25, 50));
        foreach (var mode in Modes)
        {
            AssertExact(mode, frame, Path(false, Square(60, 60, 60)));
        }
    }

    [Test]
    public void AFigureNotFilled_AddsNothing()
    {
        var unfilled = new PathGeometry();
        unfilled.Figures = new PathFigureCollection { Figure(Square(0, 0, 60), filled: false) };
        var union = Combine(GeometryCombineMode.Union, unfilled, Path(false, Square(40, 40, 60)));
        Assert.That(Covered(union, V(20, 20)), Is.False, "the unfilled figure is no area");
        Assert.That(Covered(union, V(70, 70)), Is.True);
    }

    [Test]
    public void ASmallHoleBesideALargeArea_Stays()
    {
        var withHole = Path(false, Square(0, 0, 100000), Square(10, 10, 0.4));
        var union = Combine(GeometryCombineMode.Union, withHole, Path(false, Square(50000, 50000, 60000)));
        Assert.Multiple(() =>
        {
            Assert.That(Covered(union, V(10.2, 10.2)), Is.False, "the hole");
            Assert.That(Covered(union, V(9, 9)), Is.True);
        });
    }

    [Test]
    public void ManyEdgesThroughOnePoint_MeetThere()
    {
        // Two bow ties whose four edges all cross at (50.3, 50.7), no point of the grid.
        var p = V(50.3, 50.7);
        var first = Path(false, [V(10, 10), V(90.6, 91.4), V(90.6, 10), V(10, 91.4)]);
        var second = Path(false, [p - V(40, 10), p + V(40, 10), p + V(10, -40), p - V(10, -40)]);
        foreach (var mode in Modes)
        {
            AssertExact(mode, first, second);
        }
    }

    [Test]
    public void ShapesOfALattice_AreCombinedExactly()
    {
        // Triangles and quadrilaterals on a coarse lattice: shared vertices, edges along edges, vertices on edges.
        var random = new System.Random(20261111);
        var wrong = new System.Collections.Generic.List<string>();
        for (var shape = 0; shape < 400 && wrong.Count < 5; shape++)
        {
            var a = LatticeShape(random);
            var b = LatticeShape(random);
            foreach (var mode in Modes)
            {
                var (misses, outline) = Check(mode, Path(false, a), Path(false, b), 0.3);
                if (misses > 0 || outline != null)
                {
                    wrong.Add($"{mode} [{Text(a)}] [{Text(b)}]: {misses} points{(outline != null ? ", " + outline : "")}");
                }
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    [TestCase(90)]
    [TestCase(180)]
    [TestCase(30)]
    public void TurnedOperands_AreCombinedExactly(double degrees)
    {
        // Turned a quarter or a half, an edge that was level is level up to rounding only.
        foreach (var mode in Modes)
        {
            var a = Path(false, [V(20, 30), V(60, 30), V(60, 40), V(20, 40)]);
            var b = Path(false, [V(0, 10), V(30, 10), V(30, 50), V(0, 50)]);
            a.Transform = new Transform { RotationAngle = degrees };
            b.Transform = new Transform { RotationAngle = degrees };
            AssertExact(mode, a, b);
        }

        var random = new System.Random(90180);
        var wrong = new System.Collections.Generic.List<string>();
        var turn = System.Math.PI * degrees / 180;
        Vector2<double>[] Turned(Vector2<double>[] shape) => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(shape,
            p => V(p.X * System.Math.Cos(turn) - p.Y * System.Math.Sin(turn), p.X * System.Math.Sin(turn) + p.Y * System.Math.Cos(turn))));
        for (var shape = 0; shape < 200 && wrong.Count < 5; shape++)
        {
            var (a, b) = (Turned(LatticeShape(random)), Turned(LatticeShape(random)));
            foreach (var mode in Modes)
            {
                var (misses, outline) = Check(mode, Path(false, a), Path(false, b), 0.3);
                if (misses > 0 || outline != null) wrong.Add($"{mode} [{Text(a)}] [{Text(b)}]: {misses}{(outline != null ? ", " + outline : "")}");
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    [TestCase(1e-4)]
    [TestCase(1e4)]
    public void ShapesTinyOrHuge_AreCombinedAsAtTheirOwnSize(double scale)
    {
        var random = new System.Random(4);
        var wrong = new System.Collections.Generic.List<string>();
        Vector2<double>[] Scaled(Vector2<double>[] shape) =>
            System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(shape, p => V(p.X * scale, p.Y * scale)));
        for (var shape = 0; shape < 150 && wrong.Count < 5; shape++)
        {
            var (a, b) = (Scaled(LatticeShape(random)), Scaled(LatticeShape(random)));
            foreach (var mode in Modes)
            {
                var (misses, outline) = Check(mode, Path(false, a), Path(false, b), 0.3 * scale);
                if (misses > 0 || outline != null) wrong.Add($"{mode} [{Text(a)}] [{Text(b)}]: {misses}{(outline != null && scale >= 1 ? ", " + outline : "")}");
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    [TestCase(1e-13)]
    [TestCase(1e-9)]
    [TestCase(1e-6)]
    public void ShapesJitteredByRounding_AreCombinedExactly(double jitter)
    {
        var random = new System.Random(13);
        var wrong = new System.Collections.Generic.List<string>();
        Vector2<double>[] Jittered(Vector2<double>[] shape) => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(shape,
            p => V(p.X + (random.NextDouble() - 0.5) * jitter, p.Y + (random.NextDouble() - 0.5) * jitter)));
        for (var shape = 0; shape < 200 && wrong.Count < 5; shape++)
        {
            var (a, b) = (Jittered(LatticeShape(random)), Jittered(LatticeShape(random)));
            foreach (var mode in Modes)
            {
                // A lattice shape's repeated corner, jittered by more than a contour's five decimals, is a hairline spike
                // in the shape itself: its outline is held to the shape only where the jitter is below them.
                var (misses, outline) = Check(mode, Path(false, a), Path(false, b), 0.3);
                if (jitter >= 1e-9) outline = null;
                if (misses > 0 || outline != null)
                    wrong.Add($"{mode} [{Text(a)}] [{Text(b)}]: {misses}{(outline != null ? ", " + outline : "")}");
            }
        }

        Assert.That(wrong, Is.Empty);
    }

    [Test]
    public void AGroupOperand_IsItsChildrenUnderItsRule()
    {
        var group = new GeometryGroup();
        group.Children.Add(Path(false, Square(0, 0, 100)));
        var nonZero = new GeometryGroup { FillRule = FillRule.NonZero };
        nonZero.Children.Add(Path(false, Square(0, 0, 60)));
        nonZero.Children.Add(Path(false, Square(30, 30, 60)));
        foreach (var mode in Modes)
        {
            AssertExact(mode, group, Path(false, Square(50, 50, 100)));
            AssertExact(mode, nonZero, Path(false, Square(70, 0, 50)));
        }
    }

    // Shapes whose edges run along one another or meet three to a point: points must not move off the lines they lie on.
    [TestCase(GeometryCombineMode.Xor, "50,30 30,50 10,40 50,20 0,10 40,40", "30,20 10,0 10,60 50,20 30,60 60,60")]
    [TestCase(GeometryCombineMode.Exclude, "40,40 10,60 50,10 50,60 40,40 20,0", "40,0 30,0 10,0 60,0 10,10 30,40 0,20")]
    [TestCase(GeometryCombineMode.Xor, "60,10 20,50 40,20 40,20 30,40 50,0 40,0 60,30 20,20", "10,10 40,40 20,60 20,0 60,10 60,10 60,50 30,30")]
    [TestCase(GeometryCombineMode.Exclude, "0,60 10,20 30,10 50,40 10,40 30,0 0,60", "0,50 10,0 30,10 0,50 50,40")]
    [TestCase(GeometryCombineMode.Intersect, "0,10 20,30 40,40 20,20 20,10 10,60 40,60", "60,60 30,10 10,20 30,50 30,20 60,0 0,20 0,10 20,0")]
    public void ShapesMeetingAlongLinesAndAtPoints_AreCombinedExactly(GeometryCombineMode mode, string first, string second)
    {
        AssertExact(mode, Path(false, Points(first)), Path(false, Points(second)));
        foreach (var scale in new[] { 1e-4, 1e5 })
        {
            Vector2<double>[] Scaled(string text) =>
                System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Points(text), p => V(p.X * scale, p.Y * scale)));
            var (misses, _) = Check(mode, Path(false, Scaled(first)), Path(false, Scaled(second)), 0.3 * scale);
            Assert.That(misses, Is.Zero, $"at {scale} times the size");
        }
    }

    // A shape of no area - two spikes there and back - along a turned edge of another: edges on one line up to rounding
    // must be cut alike, or the pieces on the line stop being one.
    [Test]
    public void ASpikeAlongATurnedEdge_LeavesTheOtherShape()
    {
        var spikes = Path(false, [V(51.96152422706632, 29.999999999999996), V(17.320508075688775, 9.999999999999998),
            V(51.96152422706632, 29.999999999999996), V(0, 0)]);
        var triangle = Path(false, [V(69.2820323027551, 39.99999999999999), V(34.64101615137755, 19.999999999999996),
            V(1.9615242270663273, 116.60254037844388)]);
        foreach (var mode in Modes)
        {
            AssertExact(mode, spikes, triangle);
        }
    }

    // Rings touching within a billionth: they stay apart, and the fill keeps them apart too.
    [Test]
    public void RingsTouchingWithinRounding_AreFilledAsTheyLie()
    {
        var a = Path(false, Points("9.999999999857636,10.000000000285798 29.999999999716955,9.999999999794472 " +
                                   "29.999999999668646,30.00000000018776 9.999999999840648,29.999999999776925"));
        var b = Path(false,
            Points("39.99999999996584,30.000000000380204 39.99999999968024,60.000000000383835 40.00000000048659,39.99999999963582"),
            Points("49.99999999995932,3.717378877903044E-11 50.00000000013348,20.00000000013489 " +
                   "60.0000000002949,50.00000000037338 50.00000000005561,-4.1984746927388365E-10 30.000000000331898,19.99999999954071"));
        foreach (var mode in Modes)
        {
            AssertExact(mode, a, b);
        }
    }

    // Shapes jittered by a millionth: next to no area, corners given twice a few lattice steps apart.
    [TestCase(GeometryCombineMode.Union,
        "50.000000038715335,39.99999979399894 40.00000039310834,10.00000037213187 -3.1504962677836867E-07,49.99999950976487 40.000000392064244,10.000000404895607",
        "40.0000000856095,59.99999975861303 40.00000041638083,50.00000006941984 39.999999912075694,29.999999607383668", null)]
    [TestCase(GeometryCombineMode.Union,
        "59.999999809586534,1.5264981736086767E-07 9.99999987956426,9.99999987510625 59.99999993218937,2.3481551312600055E-07 50.00000040736958,19.999999838950508 39.99999966718872,29.99999971470387",
        "30.000000376579273,59.99999977176783 49.99999983414844,10.00000042747823 49.99999955326695,39.99999951103005",
        "60.000000328065305,20.000000235721227 10.000000350428323,29.99999995629843 59.99999964008026,50.00000035947451 60.00000020564726,-3.651400401094651E-08 9.999999659730422,40.00000024047336 60.000000000313264,60.00000048526399")]
    public void ShapesJitteredByAMillionth_AreFilledAsTheyLie(GeometryCombineMode mode, string first, string second, string third)
    {
        var b = third == null ? Path(false, Points(second)) : Path(false, Points(second), Points(third));
        var (misses, _) = Check(mode, Path(false, Points(first)), b, 0.3);
        Assert.That(misses, Is.Zero);
    }

    static Vector2<double>[] Points(string text) => System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
        text.Split(' '), pair => V(double.Parse(pair.Split(',')[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(pair.Split(',')[1], System.Globalization.CultureInfo.InvariantCulture))));

    [Test]
    public void AThinOverlapFarFromTheOrigin_Stays()
    {
        const double far = 1e8;
        var a = Path(false, Square(far, far, 10));
        var b = Path(false, [V(far + 9.99, far), V(far + 20, far), V(far + 20, far + 10), V(far + 9.99, far + 10)]);
        var intersect = Combine(GeometryCombineMode.Intersect, a, b);
        Assert.That(Covered(intersect, V(far + 9.995, far + 5)), Is.True, "the 0.01 overlap");
    }

    static Vector2<double>[] LatticeShape(System.Random random)
    {
        var points = new Vector2<double>[random.Next(3, 6)];
        for (var i = 0; i < points.Length; i++)
        {
            points[i] = V(random.Next(0, 6) * 20, random.Next(0, 6) * 20);
        }

        return points;
    }

    static string Text(Vector2<double>[] points) =>
        string.Join(" ", System.Linq.Enumerable.Select(points, p => $"{p.X},{p.Y}"));

    static void AssertExact(GeometryCombineMode mode, Geometry first, Geometry second, double margin = 0.3)
    {
        var (misses, outline) = Check(mode, first, second, margin);
        Assert.Multiple(() =>
        {
            Assert.That(misses, Is.Zero, $"{mode}: points filled or empty against the operands");
            Assert.That(outline, Is.Null, mode.ToString());
        });
    }

    static (int Misses, string Outline) Check(GeometryCombineMode mode, Geometry first, Geometry second, double margin)
    {
        var a = first.Area();
        var b = second.Area();
        var mesh = Combine(mode, first, second);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var ring in System.Linq.Enumerable.Concat(a.Rings, b.Rings))
        {
            foreach (var p in ring)
            {
                (minX, minY, maxX, maxY) = (System.Math.Min(minX, p.X), System.Math.Min(minY, p.Y),
                    System.Math.Max(maxX, p.X), System.Math.Max(maxY, p.Y));
            }
        }

        var misses = 0;
        for (var y = minY - 1.13; y < maxY + 1; y += (maxY - minY + 2) / 70)
        {
            for (var x = minX - 1.17; x < maxX + 1; x += (maxX - minX + 2) / 70)
            {
                var p = V(x, y);
                if (NearAnEdge(a.Rings, p, margin) || NearAnEdge(b.Rings, p, margin))
                {
                    continue;
                }

                var (inA, inB) = (InArea(a.Rings, a.Rule, p), InArea(b.Rings, b.Rule, p));
                var expected = mode switch
                {
                    GeometryCombineMode.Union => inA || inB,
                    GeometryCombineMode.Intersect => inA && inB,
                    GeometryCombineMode.Exclude => inA && !inB,
                    _ => inA ^ inB
                };
                if (Covered(mesh, p) != expected)
                {
                    misses++;
                }
            }
        }

        return (misses, OutlineFault(mesh));
    }

    static bool InArea(System.Collections.Generic.List<Vector2<double>[]> rings, FillRule rule, Vector2<double> p)
    {
        var winding = 0;
        foreach (var ring in rings)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var (a, b) = (ring[j], ring[i]);
                var side = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (a.Y <= p.Y && b.Y > p.Y && side > 0) winding++;
                else if (b.Y <= p.Y && a.Y > p.Y && side < 0) winding--;
            }
        }

        return rule == FillRule.NonZero ? winding != 0 : (winding & 1) != 0;
    }

    static bool NearAnEdge(System.Collections.Generic.List<Vector2<double>[]> rings, Vector2<double> p, double margin)
    {
        foreach (var ring in rings)
        {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
            {
                var (a, b) = (ring[j], ring[i]);
                var d = b - a;
                var t = System.Math.Clamp(((p.X - a.X) * d.X + (p.Y - a.Y) * d.Y) / System.Math.Max(d.X * d.X + d.Y * d.Y, 1e-12), 0, 1);
                var (cx, cy) = (a.X + d.X * t - p.X, a.Y + d.Y * t - p.Y);
                if (cx * cx + cy * cy < margin * margin) return true;
            }
        }

        return false;
    }

    // A combined operand comes in through its contours, rounded to five decimals, so its cuts lie a little off the edges
    // of the other operand they came from: they are welded back onto them, and the outline runs no spike into the shape.
    [Test]
    public void ACombinedOperand_SharingEdgesWithTheOther_GivesAnOutlineWithoutSpikes()
    {
        var turned = new RectangleGeometry(new Rect(50, 0, 50, 100)) { Transform = new Transform { RotationAngle = 10 } };
        var square = new RectangleGeometry(new Rect(0, 0, 100, 100));
        var inner = new CombinedGeometry { GeometryCombineMode = GeometryCombineMode.Exclude, Geometry1 = square, Geometry2 = turned };
        var outer = new CombinedGeometry
        {
            GeometryCombineMode = GeometryCombineMode.Union,
            Geometry1 = inner,
            Geometry2 = new RectangleGeometry(new Rect(50, 0, 50, 100)) { Transform = new Transform { RotationAngle = 10 } },
        };
        outer.ProcessGeometry(GeometryType.Both);

        var inside = outer.Mesh.Contours.SelectMany(c => c.Points).Where(p => p.X > 1e-3 && p.X < 100 - 1e-3 && p.Y > 1e-3 && p.Y < 100 - 1e-3);
        Assert.That(inside, Is.Empty, "no corner of the union's outline lies inside the square");
    }

    // A rectangle's corners are rounded to four decimals; a path beside it given exactly meets it without a seam.
    [Test]
    public void ARoundedRectangle_AndAnExactPathBesideIt_UniteIntoOneRing()
    {
        var rect = new RectangleGeometry(new Rect(0, 0, 33.333304, 100));
        var path = Path(false, [V(33.333304, 0), V(100, 0), V(100, 100), V(33.333304, 100)]);
        var m = Combine(GeometryCombineMode.Union, rect, path);
        Assert.That(m.Contours, Has.Count.EqualTo(1));
    }

    // A polygon with a ring thickness is the polygon less a smaller one: it fills its band and leaves its middle.
    [Test]
    public void APolygonRing_FillsItsBand()
    {
        var ring = RegularPolygonGeometry.Build(new Rect(10.3, 10.7, 80, 80), 6, 0, 5);
        ring.ProcessGeometry(GeometryType.Both);
        Assert.Multiple(() =>
        {
            Assert.That(Covered(ring.Mesh, V(87, 50.7)), Is.True, "the band");
            Assert.That(Covered(ring.Mesh, V(50.3, 50.7)), Is.False, "the middle");
        });
    }

    static Vector2<double> V(double x, double y) => new(x, y);

    static Vector2<double>[] Square(double x, double y, double side) =>
        [V(x, y), V(x + side, y), V(x + side, y + side), V(x, y + side)];

    static PathFigure Figure(Vector2<double>[] points, bool filled = true)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = true, IsFilled = filled, Segments = new PathSegmentCollection() };
        for (var i = 1; i < points.Length; i++)
        {
            figure.Segments.Add(new LineSegment(points[i], true));
        }

        return figure;
    }

    static PathGeometry Path(bool nonZero, params Vector2<double>[][] figures)
    {
        var path = new PathGeometry { FillRule = nonZero ? FillRule.NonZero : FillRule.EvenOdd };
        var collection = new PathFigureCollection();
        foreach (var figure in figures)
        {
            collection.Add(Figure(figure));
        }

        path.Figures = collection;
        return path;
    }

    // ---- helpers ----
    static RectangleGeometry DisjointA() => new(new Rect(0, 0, 40, 40));
    static RectangleGeometry DisjointB() => new(new Rect(100, 0, 40, 40));
    static RectangleGeometry OverlapA() => new(new Rect(0, 0, 60, 60));
    static RectangleGeometry OverlapB() => new(new Rect(40, 40, 60, 60));
    static RectangleGeometry NestedOuter() => new(new Rect(0, 0, 100, 100));
    static RectangleGeometry NestedInner() => new(new Rect(30, 30, 40, 40));

    static Mesh Combine(GeometryCombineMode mode, Geometry g1, Geometry g2)
    {
        var combined = new CombinedGeometry { Geometry1 = g1, Geometry2 = g2, GeometryCombineMode = mode };
        combined.ProcessGeometry(GeometryType.Both);
        return combined.Mesh;
    }

    // Whether a triangle of the mesh covers p - its indices followed when it has them.
    static bool Covered(Mesh mesh, Vector2<double> p)
    {
        var pts = mesh.Points;
        var indices = mesh.Indices;
        var count = indices is { Length: > 0 } ? indices.Length : pts.Length;
        for (int i = 0; i + 2 < count; i += 3)
        {
            var (a, b, c) = indices is { Length: > 0 }
                ? (pts[indices[i]], pts[indices[i + 1]], pts[indices[i + 2]])
                : (pts[i], pts[i + 1], pts[i + 2]);
            if (InTri(p, (Vector2<double>)a, (Vector2<double>)b, (Vector2<double>)c)) return true;
        }

        return false;
    }

    static bool InTri(Vector2<double> p, Vector2<double> a, Vector2<double> b, Vector2<double> c)
    {
        double d1 = Sgn(p, a, b), d2 = Sgn(p, b, c), d3 = Sgn(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static double Sgn(Vector2<double> p, Vector2<double> a, Vector2<double> b) => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
}
