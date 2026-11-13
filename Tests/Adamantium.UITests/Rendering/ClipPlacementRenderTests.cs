using System;
using System.Runtime.InteropServices;
using Adamantium.Mathematics;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A group cut to a clip given in a placement's units (DrawingGroupOptions.ClipPlacement): marks on a sheet that rides a
// camera stay on the sheet as the camera moves, scales and turns - a slot write and a replay, never a record.
[TestFixture]
[Category("Gpu")]
public class ClipPlacementRenderTests
{
    private const int Width = 160;
    private const int Height = 96;

    // The sheet and the mark on it, in the camera's units: the mark runs past the sheet's right edge.
    private static readonly Rect Sheet = new(0, 0, 40, 30);
    private static readonly Rect Mark = new(20, 10, 60, 10);

    public enum MarkShape
    {
        Rectangle,
        Geometry
    }

    private sealed class MarkedSheet : MeasurableUIComponent
    {
        public readonly Placement Camera = new();
        public int Renders;
        public MarkShape Shape = MarkShape.Rectangle;
        public CornerRadius Corners = CornerRadius.Empty;

        // The sheet's clip given in the element's units instead of the camera's.
        public Rect? ElementClip;
        public Rect? OuterClip;

        protected override void OnRender(IDrawingContext context)
        {
            base.OnRender(context);
            Renders++;
            var session = context.ForControl(this);
            if (OuterClip is { } outer)
            {
                session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(outer) });
            }

            session.PushGroup(new DrawingGroupOptions
            {
                Clip = new RectangleGeometry(ElementClip ?? Sheet, Corners),
                ClipPlacement = ElementClip == null ? Camera : null
            });
            if (Shape == MarkShape.Rectangle)
            {
                session.DrawRectangle(Brushes.White, Mark, CornerRadius.Empty, null, Camera);
            }
            else
            {
                session.DrawGeometry(Brushes.White, new RectangleGeometry(Mark), null, Camera);
            }

            session.PopGroup();
            if (OuterClip != null)
            {
                session.PopGroup();
            }
        }
    }

    private readonly record struct Lit(int Count, int Left, int Top, int Right, int Bottom);

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private MarkedSheet _sheet;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Width, Height) { ClearColor = Colors.Black };
        _sheet = new MarkedSheet { Width = Width, Height = Height };
        var canvas = new Canvas();
        canvas.Children.Add(_sheet);
        _root = new VisualRoot(canvas, Width, Height);
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private static Matrix4x4F At(float x, float y) => Matrix4x4.CreateTranslation<float>(x, y, 0);

    private static Matrix4x4F QuarterTurnAt(float x, float y) =>
        Matrix4x4.CreateRotationZ<float>((float)(Math.PI / 2)) * At(x, y);

    private Lit Draw()
    {
        ((IMeasurableComponent)_root).Measure(new Size<double>(Width, Height));
        ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Width, Height));
        Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();
        return Shown();
    }

    private Lit Shown()
    {
        using var image = _renderer.RenderTarget.ResolveTexture.ToImage();
        var pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, pixels, 0, pixels.Length);
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var at = 0; at < pixels.Length; at += 4)
        {
            if (pixels[at] < 128 && pixels[at + 1] < 128 && pixels[at + 2] < 128)
            {
                continue;
            }

            var x = at / 4 % Width;
            var y = at / 4 / Width;
            count++;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return new Lit(count, left, top, right, bottom);
    }

    // What of the mark the sheet lets through under `camera`, in window pixels, cut by `outer` when given.
    private static Rect Expected(Matrix4x4F camera, Rect? outer = null)
    {
        var shown = Sheet.TransformToAABB(camera).Intersect(Mark.TransformToAABB(camera));
        return outer is { } cut ? shown.Intersect(cut) : shown;
    }

    private static void Covers(Lit lit, Rect expected, string where)
    {
        Assert.That(lit.Count, Is.GreaterThan(0), $"{where}: something is drawn");
        Assert.That(lit.Left, Is.EqualTo(expected.X).Within(1), $"{where}: left");
        Assert.That(lit.Top, Is.EqualTo(expected.Y).Within(1), $"{where}: top");
        Assert.That(lit.Right + 1, Is.EqualTo(expected.Right).Within(1), $"{where}: right");
        Assert.That(lit.Bottom + 1, Is.EqualTo(expected.Bottom).Within(1), $"{where}: bottom");
    }

    [TestCase(MarkShape.Rectangle)]
    [TestCase(MarkShape.Geometry)]
    public void AMarkPastItsSheet_IsCut_AndTheCutFollowsTheCamera_WithoutARecord(MarkShape shape)
    {
        _sheet.Shape = shape;
        var first = At(10, 10);
        _sheet.Camera.Matrix = first;
        var before = Draw();
        var renders = _sheet.Renders;

        var moved = At(70, 40);
        _sheet.Camera.Matrix = moved;
        var after = Draw();
        var replayed = _renderer.Cache.LastFrameReplayed;
        var layers = _renderer.Cache.LayersRedrawn;

        Assert.That(_renderer.RenderAgain(_root), Is.True);
        var still = _renderer.Cache.LastFrameReplayed;
        var again = Shown();

        Assert.Multiple(() =>
        {
            Covers(before, Expected(first), "first frame");
            Covers(after, Expected(moved), "camera moved");
            Assert.That(again, Is.EqualTo(after), "a frame where nothing moves draws the same");
            Assert.That(replayed, Is.True, "a camera move is a replay");
            Assert.That(still, Is.True, "a still frame is a replay");
            Assert.That(layers, Is.Zero, "cut by a scissor, no target drawn apart");
            Assert.That(_sheet.Renders, Is.EqualTo(renders), "a camera move is not a record");
        });
    }

    [Test]
    public void ScalingTheCamera_ScalesTheCut()
    {
        _sheet.Camera.Matrix = At(10, 10);
        Draw();
        var renders = _sheet.Renders;

        var zoomed = Matrix4x4.CreateScale<float>(2, 2, 1) * At(10, 10);
        _sheet.Camera.Matrix = zoomed;
        var lit = Draw();

        Assert.Multiple(() =>
        {
            Covers(lit, Expected(zoomed), "zoomed in");
            Assert.That(_renderer.Cache.LastFrameReplayed, Is.True, "a zoom is a replay");
            Assert.That(_sheet.Renders, Is.EqualTo(renders), "a zoom is not a record");
        });
    }

    [Test]
    public void ACameraTurnedByAQuarter_StillCutsByAScissor()
    {
        _sheet.Camera.Matrix = At(10, 10);
        Draw();

        var turned = QuarterTurnAt(100, 10);
        _sheet.Camera.Matrix = turned;
        var lit = Draw();

        Assert.Multiple(() =>
        {
            Covers(lit, Expected(turned), "turned");
            Assert.That(_renderer.Cache.LayersRedrawn, Is.Zero, "a quarter turn keeps the rectangle a rectangle");
        });
    }

    [Test]
    public void ACameraTurnedByAnyAngle_CutsByTheMask()
    {
        _sheet.Camera.Matrix = At(10, 10);
        Draw();

        // A sixth of a half turn about the sheet's corner: the mark leaves the sheet across its right edge, turned.
        _sheet.Camera.Matrix = Matrix4x4.CreateRotationZ<float>((float)(Math.PI / 6)) * At(40, 10);
        var lit = Draw();
        var unclipped = Mark.TransformToAABB(_sheet.Camera.Matrix);

        Assert.Multiple(() =>
        {
            Assert.That(lit.Count, Is.GreaterThan(0), "the part on the sheet is drawn");
            Assert.That(lit.Right + 1, Is.LessThan(unclipped.Right - 10), "what runs past the sheet is cut");
            Assert.That(_renderer.Cache.LayersRedrawn, Is.GreaterThan(0), "cut by a mask drawn apart");
        });
    }

    [Test]
    public void ASheetLeavingTheWindowAndComingBack_LeavesNoGhost()
    {
        var home = At(10, 10);
        _sheet.Camera.Matrix = home;
        Draw();
        var renders = _sheet.Renders;

        _sheet.Camera.Matrix = At(-300, 10);
        var gone = Draw();

        _sheet.Camera.Matrix = home;
        var back = Draw();

        Assert.Multiple(() =>
        {
            Assert.That(gone.Count, Is.Zero, "nothing drawn while the sheet is out of the window");
            Covers(back, Expected(home), "back");
            Assert.That(_sheet.Renders, Is.EqualTo(renders), "neither move is a record");
        });
    }

    [Test]
    public void AGroupInAGroup_IsCutByBoth()
    {
        var outer = new Rect(0, 0, Width, 25);
        _sheet.OuterClip = outer;
        var camera = At(10, 10);
        _sheet.Camera.Matrix = camera;
        Draw();

        var moved = At(30, 0);
        _sheet.Camera.Matrix = moved;
        var lit = Draw();

        Covers(lit, Expected(moved, outer), "cut by the element's clip and the sheet's");
    }

    [Test]
    public void ARoundedSheet_IsCutByTheMask_AndItsCutFollowsTheCamera()
    {
        _sheet.Corners = new CornerRadius(4);
        _sheet.Camera.Matrix = At(10, 10);
        var before = Draw();
        var renders = _sheet.Renders;

        var moved = At(70, 40);
        _sheet.Camera.Matrix = moved;
        var after = Draw();
        var layers = _renderer.Cache.LayersRedrawn;
        var replayed = _renderer.Cache.LastFrameReplayed;
        var rendersAfter = _sheet.Renders;

        // Recorded again where the camera stands: the group is found there, not where a stale matrix would put it.
        _sheet.InvalidateRender(false);
        var recorded = Draw();

        Assert.Multiple(() =>
        {
            Covers(before, Expected(At(10, 10)), "first frame");
            Covers(after, Expected(moved), "camera moved");
            Covers(recorded, Expected(moved), "recorded again");
            Assert.That(layers, Is.GreaterThan(0), "rounded corners are cut by a mask");
            Assert.That(replayed, Is.True, "a camera move is a replay");
            Assert.That(rendersAfter, Is.EqualTo(renders), "a camera move is not a record");
        });
    }

    // Marks on the camera inside a group cut in the element's own units: the group's region is where the marks are now.
    [Test]
    public void PlacedMarksInAGroupDrawnApart_AreFoundWhereTheyAreNow()
    {
        var clip = new Rect(0, 0, 120, 80);
        _sheet.ElementClip = clip;
        _sheet.Corners = new CornerRadius(4);
        var first = At(10, 10);
        _sheet.Camera.Matrix = first;
        var before = Draw();

        var moved = At(70, 40);
        _sheet.Camera.Matrix = moved;
        var after = Draw();

        _sheet.InvalidateRender(false);
        var recorded = Draw();

        Assert.Multiple(() =>
        {
            Covers(before, Mark.TransformToAABB(first).Intersect(clip), "first frame");
            Covers(after, Mark.TransformToAABB(moved).Intersect(clip), "camera moved");
            Covers(recorded, Mark.TransformToAABB(moved).Intersect(clip), "recorded again");
        });
    }

    [Test]
    public void AClipInAPlacement_WithATransformOfItsOwn_IsAnError()
    {
        var session = (IDrawingSession)new DrawingContext();

        Assert.Throws<ArgumentException>(() => session.PushGroup(new DrawingGroupOptions
        {
            Clip = new RectangleGeometry(Sheet), ClipPlacement = new Placement(), ClipTransform = At(1, 1)
        }));
    }
}
