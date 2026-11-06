using System.Diagnostics;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>WHAT A CAMERA MOVE COSTS on a board of a thousand things, measured on the card: how much is recorded again
/// and how long the frame takes when the plane is only panned or zoomed.</summary>
[TestFixture]
[Category("Gpu")]
public class CanvasCameraCostTests
{
    private const int Width = 800;
    private const int Height = 600;
    private const int Columns = 40;
    private const int Rows = 25;

    private OffscreenTestRenderer _renderer;
    private VisualRoot _root;
    private DrawingBoard _board;

    [SetUp]
    public void SetUp()
    {
        var device = GpuTestDevice.Device;
        _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Width, Height) { ClearColor = Colors.Black };

        _board = new DrawingBoard
        {
            Template = Template(),
            Scene = new CanvasScene(),
            Ink = Brushes.White,
            Width = Width,
            Height = Height,
            GridStyle = CanvasGridStyle.Transparent
        };

        var holder = new Canvas();
        holder.Children.Add(_board);
        _root = new VisualRoot(holder, Width, Height);
    }

    [TearDown]
    public void TearDown() => _renderer.Dispose();

    private static ControlTemplate Template() => new(() =>
    {
        var layers = new Grid();
        var front = new CanvasFrontLayer();
        var root = new Grid();

        root.Children.Add(layers);
        root.Children.Add(front);

        var result = new TemplateResult { RootComponent = root };
        result.RegisterName("PART_Layers", layers);
        result.RegisterName("PART_Front", front);
        return result;
    });

    // A thousand things, every fifth an ink stroke and the rest outlined boxes, spread so all of them are in view.
    private void Fill()
    {
        var scene = (CanvasScene)_board.Scene;

        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var x = column * 50 - Columns * 25;
                var y = row * 50 - Rows * 25;

                if ((row * Columns + column) % 5 == 0)
                {
                    var stroke = new StrokeItem(new Vector2<double>(x, y), Brushes.White, 2);
                    stroke.Add(new Vector2<double>(x, y));
                    stroke.Add(new Vector2<double>(x + 20, y + 30));
                    stroke.Add(new Vector2<double>(x + 40, y + 10));
                    scene.Add(stroke);
                    continue;
                }

                scene.Add(new ShapeItem(CanvasShape.Rectangle, new Rect(x, y, 36, 36), Brushes.White, 2,
                    new SolidColorBrush(Colors.SteelBlue)));
            }
        }
    }

    private readonly record struct Cost(double Ms, int Dirty, bool Replayed, long Updated, double Record, double Apply,
        double Layout);

    private Cost Frame()
    {
        var updated = RuntimeStats.UnitsUpdated;
        var clock = Stopwatch.StartNew();

        for (var pass = 0; pass < 2; pass++)
        {
            ((IMeasurableComponent)_root).Measure(new Size<double>(Width, Height));
            ((IMeasurableComponent)_root).Arrange(new Rect(0, 0, Width, Height));
        }

        var layout = clock.Elapsed.TotalMilliseconds;

        Assert.That(_renderer.RenderFrame(_root), Is.True, "off-screen frame must render");
        RenderDirty.Clear();

        return new Cost(clock.Elapsed.TotalMilliseconds, RuntimeStats.LastRecordDirty, _renderer.Cache.LastFrameReplayed,
            RuntimeStats.UnitsUpdated - updated, RuntimeStats.LastRecordMs, RuntimeStats.LastApplyMs, layout);
    }

    private static string Say(string what, Cost[] costs)
    {
        double ms = 0, record = 0, apply = 0, layout = 0;
        var dirty = 0;
        var replayed = 0;
        long updated = 0;

        foreach (var cost in costs)
        {
            ms += cost.Ms;
            record += cost.Record;
            apply += cost.Apply;
            layout += cost.Layout;
            dirty += cost.Dirty;
            replayed += cost.Replayed ? 1 : 0;
            updated += cost.Updated;
        }

        var n = costs.Length;
        return $"{what}: {ms / n:F2} ms a frame (layout {layout / n:F2}, record {record / n:F2}, apply {apply / n:F2}), " +
               $"{dirty / n} recorded, {updated / n} units updated, {replayed}/{n} replayed";
    }

    // A PAN AND A ZOOM ARE MATRICES. Nothing on the plane is drawn again - only the ground, one quad handed the camera,
    // and the glass - the frame replays what it holds, and not one unit is touched. The milliseconds are written out
    // beside a frame where nothing changed at all, which is what a camera move should cost on top of it: nothing.
    [Test]
    public void ACameraMoveDrawsNothingOnThePlaneAgain()
    {
        _board.Scale = 0.4;
        Fill();

        // Settled first: the first frames build everything, which is not what a camera move costs.
        for (var i = 0; i < 4; i++) Frame();

        var still = new Cost[20];
        for (var i = 0; i < still.Length; i++) still[i] = Frame();

        var pans = new Cost[20];
        for (var i = 0; i < pans.Length; i++)
        {
            _board.PanBy(new Vector2<double>(3, 2));
            pans[i] = Frame();
        }

        var zooms = new Cost[20];
        for (var i = 0; i < zooms.Length; i++)
        {
            _board.SetScaleAt(new Vector2<double>(Width / 2.0, Height / 2.0), _board.Scale * (i % 2 == 0 ? 1.05 : 1 / 1.05));
            zooms[i] = Frame();
        }

        TestContext.Out.WriteLine(Say("still", still));
        TestContext.Out.WriteLine(Say("pan", pans));
        TestContext.Out.WriteLine(Say("zoom", zooms));

        Assert.Multiple(() =>
        {
            foreach (var (what, costs) in new[] { ("pan", pans), ("zoom", zooms) })
            {
                foreach (var cost in costs)
                {
                    Assert.That(cost.Replayed, Is.True, $"a {what} walked the scene");
                    Assert.That(cost.Dirty, Is.LessThanOrEqualTo(2), $"a {what} drew more than the ground and the glass");
                    Assert.That(cost.Updated, Is.Zero, $"a {what} touched units");
                }
            }
        });
    }
}
