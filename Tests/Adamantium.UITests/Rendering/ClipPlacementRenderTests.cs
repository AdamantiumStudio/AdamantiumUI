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

    private static readonly Rect Sheet = new(0, 0, 40, 30);
    private static readonly Matrix4x4F TurnedAnyAngle =
        Matrix4x4.CreateRotationZ<float>((float)(Math.PI / 6)) * At(40, 10);
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

        public Rect? ElementClip;
        public Rect? OuterClip;

        public bool ContentStays;

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
            if (ContentStays)
            {
                session.DrawRectangle(Brushes.White, new Rect(0, 0, Width, Height));
            }
            else if (Shape == MarkShape.Rectangle)
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

    private sealed class Neighbour : MeasurableUIComponent
    {
        public Color Shade = new(40, 40, 40);

        protected override void OnRender(IDrawingContext context) =>
            context.ForControl(this).DrawRectangle(new SolidColorBrush(Shade), new Rect(0, 0, 10, 10));
    }

    private readonly record struct Lit(int Count, int Left, int Top, int Right, int Bottom);

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private MarkedSheet _sheet;
    private Neighbour _neighbour;
    private byte[] _pixels;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Width, Height) { ClearColor = Colors.Black };
        _sheet = new MarkedSheet { Width = Width, Height = Height };
        _neighbour = new Neighbour { Width = 10, Height = 10 };
        Canvas.SetLeft(_neighbour, Width - 10);
        Canvas.SetTop(_neighbour, Height - 10);
        var canvas = new Canvas();
        canvas.Children.Add(_sheet);
        canvas.Children.Add(_neighbour);
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
        _pixels = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, _pixels, 0, _pixels.Length);
        int count = 0, left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (var at = 0; at < _pixels.Length; at += 4)
        {
            if (!LitAt(at))
            {
                continue;
            }

            var column = at / 4 % Width;
            var row = at / 4 / Width;
            count++;
            left = Math.Min(left, column);
            top = Math.Min(top, row);
            right = Math.Max(right, column);
            bottom = Math.Max(bottom, row);
        }

        return new Lit(count, left, top, right, bottom);
    }

    private bool LitAt(int at) => _pixels[at] >= 128 || _pixels[at + 1] >= 128 || _pixels[at + 2] >= 128;

    private bool LitThrough(Matrix4x4F camera, double sheetX, double sheetY)
    {
        var column = (int)(sheetX * camera.M11 + sheetY * camera.M21 + camera.M41);
        var row = (int)(sheetX * camera.M12 + sheetY * camera.M22 + camera.M42);
        return LitAt((row * Width + column) * 4);
    }

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

    // A quarter turn and a mirror keep the sheet a rectangle on the window: still a scissor, still a replay.
    [TestCase(false)]
    [TestCase(true)]
    public void ACameraTurnedByAQuarterOrMirrored_StillCutsByAScissor(bool mirrored)
    {
        _sheet.Camera.Matrix = At(10, 10);
        Draw();
        var renders = _sheet.Renders;

        var turned = mirrored ? Matrix4x4.CreateScale<float>(-1, 1, 1) * At(100, 10) : QuarterTurnAt(100, 10);
        _sheet.Camera.Matrix = turned;
        var lit = Draw();

        Assert.Multiple(() =>
        {
            Covers(lit, Expected(turned), "turned");
            Assert.That(_renderer.Cache.LayersRedrawn, Is.Zero, "the rectangle stays a rectangle");
            Assert.That(_renderer.Cache.LastFrameReplayed, Is.True, "the turn is a replay");
            Assert.That(_sheet.Renders, Is.EqualTo(renders), "the turn is not a record");
        });
    }


    private void AssertCutAlongTheTurnedSheet(string where)
    {
        Assert.That(LitThrough(TurnedAnyAngle, 30, 15), Is.True, $"{where}: the mark on the sheet");
        Assert.That(LitThrough(TurnedAnyAngle, 45, 15), Is.False, $"{where}: the mark past the sheet's edge");
        Assert.That(_renderer.Cache.LayersRedrawn, Is.GreaterThan(0), $"{where}: cut by a mask drawn apart");
    }

    [Test]
    public void ACameraTurnedByAnyAngle_CutsByTheMask()
    {
        _sheet.Camera.Matrix = At(10, 10);
        Draw();

        _sheet.Camera.Matrix = TurnedAnyAngle;
        Draw();

        Assert.Multiple(() => AssertCutAlongTheTurnedSheet("turned"));
    }

    // The camera turns in a frame where something else draws again: the frame patches, and the patch must see the
    // turn too.
    [Test]
    public void ACameraTurnedWhileAnotherElementRepaints_CutsByTheMask()
    {
        _sheet.Camera.Matrix = At(10, 10);
        Draw();

        _neighbour.Shade = new Color(60, 60, 60);
        _neighbour.InvalidateRender(false);
        _sheet.Camera.Matrix = TurnedAnyAngle;
        Draw();

        Assert.Multiple(() => AssertCutAlongTheTurnedSheet("turned while patching"));
    }

    // Only the cut rides the camera; what it cuts stays where its element drew it.
    [Test]
    public void AClipAlone_OnTheCamera_MovesOverContentThatStays()
    {
        _sheet.ContentStays = true;
        var first = At(10, 10);
        _sheet.Camera.Matrix = first;
        var before = Draw();
        var renders = _sheet.Renders;

        var moved = At(70, 40);
        _sheet.Camera.Matrix = moved;
        var after = Draw();
        var replayed = _renderer.Cache.LastFrameReplayed;

        _sheet.InvalidateRender(false);
        var recorded = Draw();

        Assert.Multiple(() =>
        {
            Covers(before, Sheet.TransformToAABB(first), "first frame");
            Covers(after, Sheet.TransformToAABB(moved), "camera moved");
            Covers(recorded, Sheet.TransformToAABB(moved), "recorded again");
            Assert.That(replayed, Is.True, "a camera move is a replay");
            Assert.That(renders + 1, Is.EqualTo(_sheet.Renders), "recorded only when asked");
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
