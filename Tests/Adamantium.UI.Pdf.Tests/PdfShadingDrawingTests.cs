using System.Linq;
using Adamantium.Mathematics;
using Adamantium.UI.Core.Media;
using NUnit.Framework;
using static Adamantium.UI.Pdf.Tests.PdfPageDrawingTests;

namespace Adamantium.UI.Pdf.Tests;

/// <summary>Shadings and patterns: brushes laid in a space of their own, inside a group cut to what they paint.</summary>
public class PdfShadingDrawingTests
{
    private const string RedToBlue = "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";

    [Test]
    public void AnAxialShadingIsALinearGradientAlongItsAxis()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [10 0 110 0] /Function 6 0 R >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var drawn = session.All<DrawnGeometry>().Single();
        var brush = (LinearGradientBrush)drawn.Brush;
        Assert.That(brush.GradientStops.First().Color, Is.EqualTo(new Color(255, 0, 0, 255)));
        Assert.That(brush.GradientStops.Last().Color, Is.EqualTo(new Color(0, 0, 255, 255)));
        // Without Extend the band runs from the start to the end of the axis: u 0 at x 10, u 1 at x 110.
        var band = ((RectangleGeometry)drawn.Geometry).Rect;
        Assert.That((band.X, band.Width), Is.EqualTo((0.0, 1.0)));
        Assert.That(At(drawn.Transform, 0, 0).Item1, Is.EqualTo(10).Within(1e-4));
        Assert.That(At(drawn.Transform, 1, 0).Item1, Is.EqualTo(110).Within(1e-4));
    }

    [Test]
    public void AnExtendedAxialShadingCoversThePage_ItsStopsWhereTheAxisIs()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [50 0 150 0] /Function 6 0 R /Extend [true true] >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var drawn = session.All<DrawnGeometry>().Single();
        var band = ((RectangleGeometry)drawn.Geometry).Rect;
        Assert.That(band.X, Is.EqualTo(-0.5).Within(1e-9), "the page starts half an axis before the start");
        Assert.That(band.Right, Is.EqualTo(1.5).Within(1e-9));
        var brush = (LinearGradientBrush)drawn.Brush;
        Assert.That(brush.StartPoint.X, Is.EqualTo(0.25).Within(1e-9));
        Assert.That(brush.EndPoint.X, Is.EqualTo(0.75).Within(1e-9));
    }

    [Test]
    public void AGradientGetsAStopWhereItsFunctionBends()
    {
        // Red to green to blue, bending at a quarter: three stops follow it exactly.
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 100 0] /Function 6 0 R >>");
        pdf.Add("<< /FunctionType 3 /Domain [0 1] /Functions [7 0 R 8 0 R] /Bounds [0.25] /Encode [0 1 0 1] >>");
        pdf.Add("<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 1 0] /N 1 >>");
        pdf.Add("<< /FunctionType 2 /Domain [0 1] /C0 [0 1 0] /C1 [0 0 1] /N 1 >>");
        var (_, session) = Draw(pdf, "/S sh");
        var stops = ((LinearGradientBrush)session.All<DrawnGeometry>().Single().Brush).GradientStops;
        Assert.That(stops.Count, Is.LessThanOrEqualTo(GradientBrush.MaxRenderedStops));
        Assert.That(stops.Any(s => s.Offset == 0.25 && s.Color.G == 255), Is.True, "a stop on the bend");
    }

    [Test]
    public void AFunctionTooCurvedForTheStopsIsSampledAlongItsAxis()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 2 /ColorSpace /DeviceGray /Coords [50 0 150 0] /Function 6 0 R /Extend [true false] >>");
        // Ten waves: more turns than eight stops can follow.
        pdf.AddStream("/FunctionType 4 /Domain [0 1] /Range [0 1]", "{ 3600 mul sin 1 add 2 div }");
        var (_, session) = Draw(pdf, "/S sh");
        var strip = session.All<DrawnImage>().Single();
        Assert.That(strip.Image.Width, Is.EqualTo(256));
        Assert.That(At(strip.Transform, 0, 0).Item1, Is.EqualTo(50).Within(1e-3), "the strip spans the axis");
        Assert.That(At(strip.Transform, 1, 0).Item1, Is.EqualTo(150).Within(1e-3));
        var before = session.All<DrawnGeometry>().Single();
        Assert.That(((SolidColorBrush)before.Brush).Color.R, Is.EqualTo(128).Within(1), "the extended start in the first color");
    }

    [Test]
    public void ARadialShadingBetweenTwoCirclesIsTheBrushsOwn()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [30 50 10 60 50 40] /Function 6 0 R " +
                "/Extend [false true] >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var brush = (RadialGradientBrush)session.All<DrawnGeometry>().Single().Brush;
        Assert.That(brush.GradientOriginRadius, Is.EqualTo(0.25).Within(1e-9));
        Assert.That(brush.GradientStops.First().Color.A, Is.Zero, "nothing before the start circle");
        Assert.That(brush.GradientStops.Last().Color, Is.EqualTo(new Color(0, 0, 255, 255)), "the end goes on");
    }

    [Test]
    public void ARadialShadingShrinkingToAPointIsDrawnTheOtherWayRound()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [50 50 40 50 50 0] /Function 6 0 R " +
                "/Extend [true true] >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var brush = (RadialGradientBrush)session.All<DrawnGeometry>().Single().Brush;
        Assert.That(brush.GradientStops.First().Color, Is.EqualTo(new Color(0, 0, 255, 255)), "blue at the point");
        Assert.That(brush.GradientStops.Last().Color, Is.EqualTo(new Color(255, 0, 0, 255)));
    }

    [Test]
    public void ACircleOnTwoCirclesTakesTheLargerItIsAllowed_NotTheLargerThereIs()
    {
        // Two equal circles 60 apart, neither end extended. At x 85 the circles of s 0.917 and 1.25 pass; the shading
        // stops at 1, so the point is in the color of 0.917 - where the brush, taking 1.25, would leave it clear.
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [20 50 10 80 50 10] /Function 6 0 R >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        Assert.That(session.All<DrawnGeometry>(), Is.Empty);
        var picture = session.All<DrawnImage>().Single();
        var (r, _, b, a) = PixelAt(picture, 85, 50);
        Assert.That(a, Is.EqualTo(255));
        Assert.That(r, Is.EqualTo(21).Within(3));
        Assert.That(b, Is.EqualTo(234).Within(3));
        Assert.That(PixelAt(picture, 12, 42).A, Is.Zero, "outside the circles of 0 to 1 nothing");
    }

    [Test]
    public void ACircleShrinkingToAPointOutsideTheOtherIsWorkedOutPointByPoint()
    {
        // From a circle of 20 at x 30 to a point at x 70, both ends extended. At x 40 the circles of s -0.5 and 0.5
        // pass, and the larger wins; at x 75 only circles of a negative radius do, so it stays clear.
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [30 50 20 70 50 0] /Function 6 0 R " +
                "/Extend [true true] >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var picture = session.All<DrawnImage>().Single();
        var (r, _, b, a) = PixelAt(picture, 40, 50);
        Assert.That(a, Is.EqualTo(255));
        Assert.That(r, Is.EqualTo(128).Within(3));
        Assert.That(b, Is.EqualTo(127).Within(3));
        Assert.That(PixelAt(picture, 75, 50).A, Is.Zero);
    }

    [Test]
    public void AShadingWorkedOutPointByPoint_HasASmoothEdge()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [20 50 10 80 50 10] /Function 6 0 R >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var bitmap = (Adamantium.UI.Core.Media.Imaging.BitmapSource)session.All<DrawnImage>().Single().Image;
        var width = (int)bitmap.PixelWidth;
        var pixels = bitmap.PixelBytes;

        var partial = 0;
        var steps = 0;
        for (var row = 0; row < (int)bitmap.PixelHeight; row++)
        {
            for (var column = 1; column < width; column++)
            {
                var alpha = pixels[(row * width + column) * 4 + 3];
                var before = pixels[(row * width + column - 1) * 4 + 3];
                if (alpha is > 0 and < 255)
                {
                    partial++;
                }

                if (System.Math.Min(alpha, before) == 0 && System.Math.Max(alpha, before) == 255)
                {
                    steps++;
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(partial, Is.GreaterThan(0), "texels the edge crosses are partly covered");
            Assert.That(steps * 20, Is.LessThan(partial), "clear beside whole only where the edge runs along a side");
        });
    }

    // The picture's pixel under a point of the page, in PDF points (y up from the bottom of the 100-point page).
    private static (int R, int G, int B, int A) PixelAt(DrawnImage drawn, double x, double y)
    {
        var bitmap = (Adamantium.UI.Core.Media.Imaging.BitmapSource)drawn.Image;
        var (ox, oy) = At(drawn.Transform, 0, 0);
        var (ux, uy) = At(drawn.Transform, 1, 0);
        var (vx, vy) = At(drawn.Transform, 0, 1);
        var (ax, ay, bx, by) = (ux - ox, uy - oy, vx - ox, vy - oy);
        var (px, py) = (x - ox, 100 - y - oy);
        var det = ax * by - bx * ay;
        var u = (px * by - bx * py) / det;
        var v = (ax * py - px * ay) / det;
        var column = (int)(u * bitmap.PixelWidth);
        var row = (int)(v * bitmap.PixelHeight);
        var at = (row * (int)bitmap.PixelWidth + column) * 4;
        var pixels = bitmap.PixelBytes;
        return (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]);
    }

    [Test]
    public void ARadialShadingFromAPointIsTheBrushsFocus()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords [55 50 0 50 50 20] /Function 6 0 R >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var drawn = session.All<DrawnGeometry>().Single();
        var brush = (RadialGradientBrush)drawn.Brush;
        Assert.That(drawn.Geometry, Is.InstanceOf<EllipseGeometry>());
        Assert.That(brush.Center, Is.EqualTo(new Vector2<double>(0.5, 0.5)));
        Assert.That(brush.GradientOrigin.X, Is.EqualTo(0.625).Within(1e-9));
        Assert.That(brush.RadiusX, Is.EqualTo(0.5).Within(1e-9));
    }

    [Test]
    public void AFunctionShadingIsSampledIntoAPictureOverItsDomain()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.Add("<< /ShadingType 1 /ColorSpace /DeviceGray /Domain [0 10 0 20] /Function 6 0 R >>");
        pdf.AddStream("/FunctionType 4 /Domain [0 10 0 20] /Range [0 1]", "{ pop 10 div }");
        var (_, session) = Draw(pdf, "/S sh");
        var image = session.All<DrawnImage>().Single();
        Assert.That(At(image.Transform, 1, 1), Is.EqualTo((10.0, 80.0)));
    }

    [Test]
    public void AShadingPatternFillsThroughAGroupCutToThePath()
    {
        var pdf = new TestPdf { Resources = "<< /Pattern << /P 5 0 R >> >>" };
        pdf.Add("<< /PatternType 2 /Shading 6 0 R /Matrix [1 0 0 1 10 0] >>");
        pdf.Add("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 100 0] /Function 7 0 R /Background [0 1 0] >>");
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/Pattern cs /P scn 0 0 30 30 re f");
        var group = session.All<OpenedGroup>().Single();
        Assert.That(((RectangleGeometry)group.Options.Clip).Rect.Width, Is.EqualTo(30));
        var inside = session.All<DrawnGeometry>().ToArray();
        Assert.That(((SolidColorBrush)inside[0].Brush).Color, Is.EqualTo(new Color(0, 255, 0, 255)), "background");
        Assert.That(inside[1].Brush, Is.InstanceOf<LinearGradientBrush>());
        Assert.That(At(inside[1].Transform, 0, 0).Item1, Is.EqualTo(10).Within(1e-4), "the pattern's own matrix");
    }

    [Test]
    public void AFreeFormMeshIsItsTriangles_EachCornerItsColor()
    {
        // One triangle: 8-bit flag, x, y and RGB per vertex, coordinates 0..255 as written.
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.AddStream("/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 " +
                      "/Decode [0 255 0 255 0 1 0 1 0 1]",
            new byte[] { 0, 0, 0, 255, 0, 0, 0, 100, 0, 0, 255, 0, 0, 0, 50, 0, 0, 255 });
        var (_, session) = Draw(pdf, "/S sh");
        var mesh = session.All<DrawnMesh>().Single();
        Assert.That(mesh.Mesh.Vertices, Has.Count.EqualTo(3));
        Assert.That(mesh.Mesh.Vertices.Select(v => v.Color), Is.EqualTo(new[]
        {
            new Color(255, 0, 0, 255), new Color(0, 255, 0, 255), new Color(0, 0, 255, 255),
        }));
        Assert.That(At(mesh.Transform, 100, 0), Is.EqualTo((100.0, 100.0)));
    }

    [Test]
    public void ASeeThroughMeshIsSeenThroughAsOne_ItsCornersOpaque()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> /ExtGState << /G 6 0 R >> >>" };
        pdf.AddStream("/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 " +
                      "/Decode [0 255 0 255 0 1 0 1 0 1]",
            new byte[] { 0, 0, 0, 255, 0, 0, 0, 100, 0, 0, 255, 0, 0, 0, 50, 0, 0, 255 });
        pdf.Add("<< /ca 0.5 >>");
        var (_, session) = Draw(pdf, "/G gs /S sh");
        Assert.That(session.All<OpenedGroup>().Single(g => g.Options.Opacity < 1).Options.Opacity, Is.EqualTo(0.5));
        Assert.That(session.All<DrawnMesh>().Single().Mesh.Vertices.Select(v => v.Color.A), Is.All.EqualTo(255));
    }

    [Test]
    public void AMeshInACalibratedSpaceIsCutFiner()
    {
        // Colors straight in sRGB only for DeviceRGB and DeviceGray: a CalRGB triangle is cut.
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.AddStream("/ShadingType 4 /ColorSpace [/CalRGB << /WhitePoint [0.9505 1 1.089] /Gamma [2.2 2.2 2.2] >>] " +
                      "/BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 255 0 255 0 1 0 1 0 1]",
            new byte[] { 0, 0, 0, 255, 0, 0, 0, 100, 0, 0, 255, 0, 0, 0, 50, 0, 0, 255 });
        var (_, session) = Draw(pdf, "/S sh");
        Assert.That(session.All<DrawnMesh>().Single().Mesh.Vertices.Count, Is.GreaterThan(3));
    }

    [Test]
    public void AMeshWhoseColorsComeThroughAFunctionIsCutFiner()
    {
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.AddStream("/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 " +
                      "/Decode [0 255 0 255 0 1] /Function 6 0 R",
            new byte[] { 0, 0, 0, 0, 0, 100, 0, 255, 0, 0, 50, 128 });
        pdf.Add(RedToBlue);
        var (_, session) = Draw(pdf, "/S sh");
        var mesh = session.All<DrawnMesh>().Single().Mesh;
        Assert.That(mesh.Vertices.Count, Is.GreaterThan(3));
        Assert.That(mesh.Vertices.Count % 3, Is.Zero);
    }

    [Test]
    public void ACoonsPatchIsSampledOnAGrid_ItsCornersAtThePatchsCorners()
    {
        // A flat square 0..60 by its twelve boundary points, the corners red, green, blue and white.
        var points = new[] { 0, 1, 2, 3, 7, 11, 15, 14, 13, 12, 8, 4 };
        var data = new System.Collections.Generic.List<byte> { 0 };
        foreach (var index in points)
        {
            data.Add((byte)(index / 4 * 20));
            data.Add((byte)(index % 4 * 20));
        }

        data.AddRange(new byte[] { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 });
        var pdf = new TestPdf { Resources = "<< /Shading << /S 5 0 R >> >>" };
        pdf.AddStream("/ShadingType 6 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 " +
                      "/Decode [0 255 0 255 0 1 0 1 0 1]", data.ToArray());
        var (_, session) = Draw(pdf, "/S sh");
        var mesh = session.All<DrawnMesh>().Single().Mesh;
        Assert.That(mesh.Vertices.Count, Is.GreaterThan(6), "cut into a grid");
        var corner = mesh.Vertices.First(v => v.Position == new Vector2<double>(0, 0));
        Assert.That(corner.Color, Is.EqualTo(new Color(255, 0, 0, 255)), "c00 at p00");
        var far = mesh.Vertices.First(v => v.Position == new Vector2<double>(60, 60));
        Assert.That(far.Color, Is.EqualTo(new Color(0, 0, 255, 255)), "c33 at p33");
        // Rows of v first: the second piece is the next one along u, still on the row v = 0.
        Assert.That(mesh.Vertices[6].Position.Y, Is.Zero);
        Assert.That(mesh.Vertices[6].Position.X, Is.GreaterThan(0));
        Assert.That((mesh.Bounds.X, mesh.Bounds.Y), Is.EqualTo((0.0, 0.0)));
        Assert.That(mesh.Bounds.Width, Is.EqualTo(60).Within(1e-9));
        Assert.That(mesh.Bounds.Height, Is.EqualTo(60).Within(1e-9));
    }

    [Test]
    public void ATilingPatternsCellIsDrawnOnceAndTiledByABrush()
    {
        var pdf = new TestPdf { Resources = "<< /Pattern << /P 5 0 R >> >>" };
        pdf.AddStream("/PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 4 4] /XStep 5 /YStep 5 /Resources << >>",
            "1 0 0 rg 0 0 2 2 re f");
        var (_, session) = Draw(pdf, "/Pattern cs /P scn 0 0 30 30 re f 40 0 10 10 re f");
        var fills = session.All<DrawnGeometry>().ToArray();
        Assert.That(fills, Has.Length.EqualTo(2));
        var brushes = fills.Select(f => (DrawingBrush)f.Brush).ToArray();
        Assert.That(brushes[0].TileMode, Is.EqualTo(TileMode.Tile));
        var area = ((RectangleGeometry)fills[0].Geometry).Rect;
        Assert.That(brushes[0].ViewportUnits, Is.EqualTo(BrushMappingMode.RelativeToBoundingBox));
        Assert.That(brushes[0].Viewport.Width * area.Width, Is.EqualTo(5).Within(1e-9), "a step, in pattern space");
        Assert.That(area.X + brushes[0].Viewport.X * area.Width, Is.EqualTo(0).Within(1e-9), "on the cell's corner");
        Assert.That(brushes[0].Drawing, Is.SameAs(brushes[1].Drawing));
        var cell = new RecordingSession();
        brushes[0].Drawing.Render(cell, Matrix4x4<float>.Identity, null);
        Assert.That(((SolidColorBrush)cell.All<DrawnGeometry>().Single().Brush).Color.R, Is.EqualTo(255));
    }

    [Test]
    public void AnUncoloredPatternsCellTakesTheColorGivenWithIt()
    {
        var pdf = new TestPdf { Resources = "<< /Pattern << /P 5 0 R >> /ColorSpace << /U [/Pattern /DeviceRGB] >> >>" };
        pdf.AddStream("/PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 4 4] /XStep 5 /YStep 5 /Resources << >>",
            "0 0 2 2 re f");
        var (_, session) = Draw(pdf, "/U cs 0 1 0 /P scn 0 0 30 30 re f");
        var cell = new RecordingSession();
        ((DrawingBrush)session.All<DrawnGeometry>().Single().Brush).Drawing.Render(cell, Matrix4x4<float>.Identity,
            null);
        Assert.That(((SolidColorBrush)cell.All<DrawnGeometry>().Single().Brush).Color, Is.EqualTo(new Color(0, 255, 0, 255)));
    }
}
