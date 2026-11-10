using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Adamantium.Fonts;
using Adamantium.Graphics.Core;
using Adamantium.Graphics.Fonts;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// Groups of draws inside one element (PushGroup/PopGroup) as the renderer lays them down: cut by a path, a rectangle
// and glyphs, let through a soft mask, faded as a whole and mixed by every blend mode as ISO 32000-2, 11.3.5 says -
// the formulas written here on their own. And placed glyphs drawn where their matrix puts them.
[TestFixture]
[Category("Gpu")]
public class DrawingGroupRenderTests
{
    private const int Dim = 96;
    private const int Tolerance = 3;

    private static readonly Color Red = Colors.Red;

    private sealed class DrawnView(Action<IDrawingSession> draw) : MeasurableUIComponent
    {
        protected override Size<double> MeasureOverride(Size<double> availableSize) => new(Dim, Dim);

        protected override void OnRender(IDrawingContext context) => draw(context.ForControl(this));
    }

    private sealed class Frame(byte[] pixels, int layers)
    {
        public int Layers { get; } = layers;

        public (int R, int G, int B) At(int x, int y)
        {
            var at = (y * Dim + x) * 4;
            return (pixels[at + 2], pixels[at + 1], pixels[at]);
        }
    }

    private static Frame Render(Action<IDrawingSession> draw, bool again = false, double opacity = 1)
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.White };
        var root = new VisualRoot(new DrawnView(draw) { Opacity = opacity }, Dim, Dim);
        ((IMeasurableComponent)root).Measure(new Size<double>(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));
        Assert.That(renderer.RenderFrame(root), Is.True, "off-screen frame must render");
        var layers = renderer.Cache.LayersRedrawn;
        if (again)
        {
            Assert.That(renderer.RenderAgain(root), Is.True);
            Assert.That(renderer.Cache.LastFrameReplayed, Is.True, "the second frame replays the first");
        }

        using var image = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, bytes, 0, bytes.Length);
        return new Frame(bytes, layers);
    }

    private static Geometry Triangle()
    {
        var geometry = new StreamGeometry { FillRule = FillRule.NonZero };
        var context = geometry.Open();
        context.BeginFigure(new Vector2<double>(0, 0), true, true)
            .LineTo(new Vector2<double>(Dim, 0))
            .LineTo(new Vector2<double>(0, Dim))
            .CloseFigure();
        geometry.RecalculateBounds();
        return geometry;
    }

    private static void Fill(IDrawingSession session, Color color, Rect rect) =>
        session.DrawRectangle(new SolidColorBrush(color), rect);

    private static void Near((int R, int G, int B) drawn, (int R, int G, int B) wanted, string where)
    {
        var off = Math.Max(Math.Abs(drawn.R - wanted.R), Math.Max(Math.Abs(drawn.G - wanted.G), Math.Abs(drawn.B - wanted.B)));
        Assert.That(off, Is.LessThanOrEqualTo(Tolerance), $"{where}: drawn {drawn}, wanted {wanted}");
    }

    private static readonly (int, int, int) White = (255, 255, 255);
    private static readonly (int, int, int) Solid = (255, 0, 0);

    [Test]
    public void APathClip_CutsEverythingTheGroupDraws()
    {
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = Triangle() });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        });

        Assert.Multiple(() =>
        {
            Near(frame.At(12, 12), Solid, "inside the triangle");
            Near(frame.At(Dim - 12, Dim - 12), White, "outside it");
            Assert.That(frame.Layers, Is.GreaterThanOrEqualTo(2), "a mask and the content, drawn apart");
        });
    }

    [Test]
    public void ARectangleClip_CutsByAScissor_WithNoLayer()
    {
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = new RectangleGeometry(new Rect(16, 16, 32, 32)) });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        });

        Assert.Multiple(() =>
        {
            Near(frame.At(24, 24), Solid, "inside the rectangle");
            Near(frame.At(8, 8), White, "above and left of it");
            Near(frame.At(60, 60), White, "below and right of it");
            Assert.That(frame.Layers, Is.EqualTo(0), "cut by the scissor alone");
        });
    }

    [Test]
    public void Opacity_FadesTheGroupAsAWhole()
    {
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
            Fill(session, Red, new Rect(0, 0, 60, Dim));
            Fill(session, Red, new Rect(36, 0, 60, Dim));
            session.PopGroup();
        });

        Assert.Multiple(() =>
        {
            Near(frame.At(12, 40), (255, 128, 128), "one shape");
            Near(frame.At(48, 40), (255, 128, 128), "two overlapping: the group fades once, not each shape");
        });
    }

    public static IEnumerable<TestCaseData> Modes() =>
        Enum.GetValues<BlendMode>().Select(m => new TestCaseData(m).SetName($"Blend{m}"));

    [TestCaseSource(nameof(Modes))]
    public void EachBlendMode_MixesAsTheFormulasSay(BlendMode mode)
    {
        var backdrop = new Color(51, 153, 230);
        var source = new Color(204, 77, 128, 191);
        var frame = Render(session =>
        {
            Fill(session, backdrop, new Rect(0, 0, Dim, Dim));
            session.PushGroup(new DrawingGroupOptions { BlendMode = mode });
            Fill(session, source, new Rect(Dim / 2.0, 0, Dim / 2.0, Dim));
            session.PopGroup();
        });

        var wanted = Composite(mode, source, backdrop);
        Assert.Multiple(() =>
        {
            Near(frame.At(Dim * 3 / 4, Dim / 2), wanted, $"{mode} where the group lies");
            Near(frame.At(Dim / 4, Dim / 2), (backdrop.R, backdrop.G, backdrop.B), "the backdrop beside it, as it was");
        });
    }

    [Test]
    public void ALuminosityMask_ShowsTheGroupWhereItIsLight()
    {
        var mask = new GeometryDrawing
        {
            Brush = Brushes.White,
            Geometry = new RectangleGeometry(new Rect(0, 0, Dim / 2.0, Dim))
        };
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions
            {
                Mask = mask, MaskKind = MaskKind.Luminosity, MaskBackdrop = Colors.Black
            });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        });

        Assert.Multiple(() =>
        {
            Near(frame.At(Dim / 4, Dim / 2), Solid, "under white");
            Near(frame.At(Dim * 3 / 4, Dim / 2), White, "under the black backdrop");
        });
    }

    [Test]
    public void AnAlphaMask_LetsTheGroupThroughAsMuchAsItCovers()
    {
        var mask = new GeometryDrawing
        {
            Brush = new SolidColorBrush(new Color(0, 0, 0, 128)),
            Geometry = new RectangleGeometry(new Rect(0, 0, Dim, Dim))
        };
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Mask = mask, MaskKind = MaskKind.Alpha });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        });

        Near(frame.At(Dim / 2, Dim / 2), (255, 127, 127), "half covered");
    }

    private static GlyphRun Bar(double size, double x, double baseline)
    {
        var typeface = Typeface.LoadFont(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fonts",
            "RobotoFlex-Variable.ttf"), 3);
        var font = typeface.GetFont(0);
        return new GlyphRun(font, size, [new PositionedGlyph(font.GetGlyphByCharacter('l').Index, x, baseline)]);
    }

    [Test]
    public void ATextClip_ShowsTheGroupThroughTheGlyphs()
    {
        var run = Bar(80, 30, 84);
        var bounds = run.Bounds;
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { TextClip = [new PlacedGlyphRun(run, Matrix4x4F.Identity)] });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        });

        var middle = (int)(bounds.Y + bounds.Height / 2);
        Assert.Multiple(() =>
        {
            Assert.That(bounds.Width, Is.GreaterThan(4), "the letter has a stem to look through");
            Near(frame.At((int)(bounds.X + bounds.Width / 2), middle), Solid, "through the stem");
            Near(frame.At((int)bounds.X - 6, middle), White, "beside it");
        });
    }

    [Test]
    public void PlacedGlyphs_AreDrawnWhereTheirMatrixPutsThem()
    {
        var run = Bar(60, 0, 60);
        var bounds = run.Bounds;
        var frame = Render(session =>
            session.DrawGlyphs(run, Brushes.Black, null, Matrix4x4.CreateTranslation<float>(40, 10, 0)));

        var y = (int)(10 + bounds.Y + bounds.Height / 2);
        Assert.Multiple(() =>
        {
            Assert.That(frame.At((int)(40 + bounds.X + bounds.Width / 2), y).R, Is.LessThan(64), "the stem, moved");
            Assert.That(frame.At((int)(bounds.X + bounds.Width / 2), y).R, Is.GreaterThan(240), "nothing where it was");
        });
    }

    [Test]
    public void NestedGroups_AreCutByTheOuterAndFadedByTheInner()
    {
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = Triangle() });
            session.PushGroup(new DrawingGroupOptions { Opacity = 0.5 });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
            session.PopGroup();
        });

        Assert.Multiple(() =>
        {
            Near(frame.At(12, 12), (255, 128, 128), "inside the outer clip, faded by the inner group");
            Near(frame.At(Dim - 12, Dim - 12), White, "outside the outer clip");
        });
    }

    [Test]
    public void ABlendingGroup_InsideAClip_MixesWithWhatTheOuterGroupDrew()
    {
        var backdrop = new Color(51, 153, 230);
        var source = new Color(204, 77, 128);
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = Triangle() });
            Fill(session, backdrop, new Rect(0, 0, Dim, Dim));
            // Wider than the outer group's region: its copy of what lies under it can only come from inside that region.
            session.PushGroup(new DrawingGroupOptions { BlendMode = BlendMode.Multiply });
            Fill(session, source, new Rect(-20, -20, Dim + 40, Dim + 40));
            session.PopGroup();
            session.PopGroup();
        });

        Assert.Multiple(() =>
        {
            Near(frame.At(12, 12), Composite(BlendMode.Multiply, source, backdrop), "multiplied, inside the clip");
            Near(frame.At(Dim - 12, Dim - 12), White, "outside the clip");
        });
    }

    // The element's opacity fades what the group shows once: its masks are drawn at full strength.
    [Test]
    public void AnElementsOpacity_FadesItsGroupOnce()
    {
        var mask = new GeometryDrawing
        {
            Brush = Brushes.White,
            Geometry = new RectangleGeometry(new Rect(0, 0, Dim, Dim))
        };
        var clipped = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = Triangle() });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        }, opacity: 0.5);
        var masked = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions
            {
                Mask = mask, MaskKind = MaskKind.Luminosity, MaskBackdrop = Colors.Black
            });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        }, opacity: 0.5);

        Assert.Multiple(() =>
        {
            Near(clipped.At(12, 12), (255, 128, 128), "cut by a path, at half");
            Near(masked.At(Dim / 2, Dim / 2), (255, 128, 128), "through a white mask, at half");
        });
    }

    [Test]
    public void AGroupLeftOpen_EndsWithItsElementsDrawing()
    {
        var frame = Render(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = Triangle() });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
        });

        Near(frame.At(Dim - 12, Dim - 12), White, "cut as if it had been closed");
    }

    [Test]
    public void AReplayedFrame_LaysTheGroupsDownAgain()
    {
        var frame = Render(session =>
        {
            Fill(session, new Color(51, 153, 230), new Rect(0, 0, Dim, Dim));
            session.PushGroup(new DrawingGroupOptions { Clip = Triangle(), BlendMode = BlendMode.Multiply });
            Fill(session, new Color(204, 77, 128), new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        }, again: true);

        Assert.Multiple(() =>
        {
            Near(frame.At(12, 12), Composite(BlendMode.Multiply, new Color(204, 77, 128), new Color(51, 153, 230)),
                "the group, multiplied");
            Near(frame.At(Dim - 12, Dim - 12), (51, 153, 230), "the backdrop outside the clip");
        });
    }

    // Scrolling moves an element without recording it again; its groups were laid out where it stood, so the frame after
    // the move has to lay them out anew - not lay them down over the old place.
    [Test]
    public void AnElementMoved_LaysItsGroupsDownWhereItIsNow()
    {
        var device = GpuTestDevice.Device;
        var factory = new RenderUnitFactory(device, new DeviceResourceFactory(device));
        using var renderer = new OffscreenTestRenderer(device, factory, Dim, Dim) { ClearColor = Colors.White };
        // A strip 30 high: moved 40 down, where it is now and where it was do not meet.
        var strip = new StreamGeometry { FillRule = FillRule.NonZero };
        strip.Open().BeginFigure(new Vector2<double>(0, 0), true, true)
            .LineTo(new Vector2<double>(Dim, 0)).LineTo(new Vector2<double>(0, 30)).CloseFigure();
        strip.RecalculateBounds();
        var view = new DrawnView(session =>
        {
            session.PushGroup(new DrawingGroupOptions { Clip = strip });
            Fill(session, Red, new Rect(0, 0, Dim, Dim));
            session.PopGroup();
        });
        var host = new Adamantium.UI.Controls.Panels.StackPanel();
        host.Children.Add(view);
        var root = new VisualRoot(host, Dim, Dim);
        ((IMeasurableComponent)root).Measure(new Size<double>(Dim, Dim));
        ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));
        Assert.That(renderer.RenderFrame(root), Is.True);

        // The element's parent moves, as a scrolled panel does: the element itself is not recorded again.
        ((IMeasurableComponent)host).Arrange(new Rect(0, 40, Dim, Dim));
        Assert.That(renderer.RenderFrame(root), Is.True);
        Assert.That(renderer.Cache.LastBuildKind, Is.EqualTo(RenderBuildKind.Partial), "a move, not a new recording");

        using var image = renderer.RenderTarget.ResolveTexture.ToImage();
        var bytes = new byte[(int)image.TotalSizeInBytes];
        Marshal.Copy(image.DataPointer, bytes, 0, bytes.Length);
        var frame = new Frame(bytes, 0);
        Assert.Multiple(() =>
        {
            Near(frame.At(8, 46), Solid, "the strip, moved down with the element");
            Near(frame.At(8, 10), White, "where the element no longer is");
        });
    }

    [Test]
    public void ARunsOutline_LiesOverItsInk()
    {
        var run = Bar(60, 10, 70);

        var outline = run.Outline.Bounds;

        Assert.Multiple(() =>
        {
            Assert.That(outline.X, Is.EqualTo(run.Bounds.X).Within(0.5));
            Assert.That(outline.Y, Is.EqualTo(run.Bounds.Y).Within(0.5));
            Assert.That(outline.Width, Is.EqualTo(run.Bounds.Width).Within(0.5));
            Assert.That(outline.Height, Is.EqualTo(run.Bounds.Height).Within(0.5));
        });
    }

    [Test]
    public void PopGroup_WithNoGroup_IsAnError()
    {
        var session = (IDrawingSession)new DrawingContext();

        Assert.Throws<InvalidOperationException>(() => session.PopGroup());
    }

    // ISO 32000-2, 11.3.5 and 11.3.6, over an opaque backdrop.
    private static (int R, int G, int B) Composite(BlendMode mode, Color source, Color backdrop)
    {
        var a = source.A / 255.0;
        double[] cs = [source.R / 255.0, source.G / 255.0, source.B / 255.0];
        double[] cb = [backdrop.R / 255.0, backdrop.G / 255.0, backdrop.B / 255.0];
        var mixed = Blend(mode, cb, cs);
        return (Byte(0), Byte(1), Byte(2));

        int Byte(int i) => (int)Math.Round(((1 - a) * cb[i] + a * mixed[i]) * 255);
    }

    private static double[] Blend(BlendMode mode, double[] cb, double[] cs) => mode switch
    {
        BlendMode.Hue => SetLum(SetSat(cs, Sat(cb)), Lum(cb)),
        BlendMode.Saturation => SetLum(SetSat(cb, Sat(cs)), Lum(cb)),
        BlendMode.Color => SetLum(cs, Lum(cb)),
        BlendMode.Luminosity => SetLum(cb, Lum(cs)),
        _ => [Separable(mode, cb[0], cs[0]), Separable(mode, cb[1], cs[1]), Separable(mode, cb[2], cs[2])],
    };

    private static double Separable(BlendMode mode, double b, double s) => mode switch
    {
        BlendMode.Normal => s,
        BlendMode.Multiply => b * s,
        BlendMode.Screen => b + s - b * s,
        BlendMode.Overlay => HardLight(s, b),
        BlendMode.Darken => Math.Min(b, s),
        BlendMode.Lighten => Math.Max(b, s),
        BlendMode.ColorDodge => b == 0 ? 0 : s >= 1 ? 1 : Math.Min(1, b / (1 - s)),
        BlendMode.ColorBurn => b >= 1 ? 1 : s == 0 ? 0 : 1 - Math.Min(1, (1 - b) / s),
        BlendMode.HardLight => HardLight(b, s),
        BlendMode.SoftLight => s <= 0.5
            ? b - (1 - 2 * s) * b * (1 - b)
            : b + (2 * s - 1) * ((b <= 0.25 ? ((16 * b - 12) * b + 4) * b : Math.Sqrt(b)) - b),
        BlendMode.Difference => Math.Abs(b - s),
        BlendMode.Exclusion => b + s - 2 * b * s,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static double HardLight(double b, double s) => s <= 0.5 ? b * 2 * s : b + (2 * s - 1) - b * (2 * s - 1);

    private static double Lum(double[] c) => 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2];

    private static double[] SetLum(double[] c, double l)
    {
        var d = l - Lum(c);
        double[] result = [c[0] + d, c[1] + d, c[2] + d];
        var lum = Lum(result);
        var min = result.Min();
        var max = result.Max();
        for (var i = 0; i < 3; i++)
        {
            if (min < 0) result[i] = lum + (result[i] - lum) * lum / (lum - min);
            if (max > 1) result[i] = lum + (result[i] - lum) * (1 - lum) / (max - lum);
        }

        return result;
    }

    private static double Sat(double[] c) => c.Max() - c.Min();

    private static double[] SetSat(double[] c, double s)
    {
        var min = c.Min();
        var max = c.Max();
        return max > min ? c.Select(v => (v - min) * s / (max - min)).ToArray() : [0, 0, 0];
    }
}
