using System.Collections.Generic;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

// A scene whose recorded stream is long - hundreds of draws of their own - still patches a change in the count of one
// element's draws: the stream may grow past what its last walk recorded before it is walked again, not past a fixed size.
[TestFixture]
[Category("Gpu")]
public class LongStreamSpliceTests
{
    private const int Dim = 128;

    private sealed class Lines : MeasurableUIComponent
    {
        public int Count;

        protected override void OnRender(IDrawingContext context)
        {
            var session = context.ForControl(this);
            var pen = new Pen(Brushes.White, 1);
            for (var i = 0; i < Count; i++)
            {
                var y = 2 + i % 120;
                session.DrawLine(new Vector2<double>(2, y), new Vector2<double>(60 + i % 50, y), pen);
            }
        }
    }

    private sealed class Squares : MeasurableUIComponent
    {
        public int Count = 1;

        protected override void OnRender(IDrawingContext context)
        {
            var session = context.ForControl(this);
            for (var i = 0; i < Count; i++)
            {
                session.DrawRectangle(Brushes.Red, new Rect(70 + i * 6, 10, 4, 4));
            }
        }
    }

    [Test]
    public void ALongStream_StillSplicesAChangedCountOfDraws()
    {
        var device = GpuTestDevice.Device;
        using var renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
            Dim, Dim) { ClearColor = Colors.Black };
        var lines = new Lines { Width = Dim, Height = Dim, Count = 400 };
        var squares = new Squares { Width = Dim, Height = Dim };
        var canvas = new Canvas();
        canvas.Children.Add(lines);
        canvas.Children.Add(squares);
        var root = new VisualRoot(canvas, Dim, Dim);

        void Draw()
        {
            ((IMeasurableComponent)root).Measure(new Size<double>(Dim, Dim));
            ((IMeasurableComponent)root).Arrange(new Rect(0, 0, Dim, Dim));
            Assert.That(renderer.RenderFrame(root), Is.True);
            RenderDirty.Clear();
        }

        Draw();
        Draw();
        Assume.That(renderer.Cache.RecordedOpCount, Is.GreaterThan(256), "the stream must be long for this to say anything");
        var refusals = new List<string>();
        var traced = Adamantium.UI.Core.Diagnostics.FrameTrace.Enabled;
        Adamantium.UI.Core.Diagnostics.FrameTrace.Enabled = true;
        try
        {
            for (var step = 2; step <= 4; step++)
            {
                Adamantium.UI.Core.Diagnostics.FrameTrace.Refuser = null;
                squares.Count = step;
                squares.InvalidateRender(false);
                Draw();
                if (!renderer.Cache.LastFrameReplayed) refusals.Add(Adamantium.UI.Core.Diagnostics.FrameTrace.Refuser);
            }
        }
        finally
        {
            Adamantium.UI.Core.Diagnostics.FrameTrace.Enabled = traced;
        }

        Assert.That(refusals, Is.Empty, "a draw added to one element is a splice, however long the stream is");
    }
}
