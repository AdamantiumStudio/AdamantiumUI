using System;
using System.Runtime.CompilerServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Extensions;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>A view swapped out of a window must not be kept by the window's render cache.</summary>
[TestFixture]
[Category("Gpu")]
public class DepartedViewReleaseTests
{
    private const int Width = 200;
    private const int Height = 150;

    private sealed class Root : Grid, IRootVisualComponent
    {
        public Vector2 PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2 point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private sealed class Stage : IDisposable
    {
        private readonly RenderDirtyScope _scope = RenderDirtyRouter.NewScope();
        private readonly OffscreenTestRenderer _renderer;

        public Root Root { get; } = new() { ClientWidth = Width, ClientHeight = Height };
        public ContentPresenter Presenter { get; } = new();

        public Stage()
        {
            var device = GpuTestDevice.Device;
            _renderer = new OffscreenTestRenderer(device, new RenderUnitFactory(device, new DeviceResourceFactory(device)),
                Width, Height);
            Root.Children.Add(Presenter);
            Root.ClaimRenderScope(_scope);
            _renderer.Cache.Dirty = _scope;
        }

        public void Frame()
        {
            Root.Measure(new Size(Width, Height), force: true);
            Root.Arrange(new Rect(0, 0, Width, Height));
            WindowExtension.UpdateTree(Root);
            Assert.That(_renderer.RenderFrame(Root), Is.True, "the frame must be drawn");
            RenderDirty.Clear();
        }

        public void Dispose()
        {
            _renderer.Dispose();
            RenderDirtyRouter.Forget(_scope);
        }
    }

    private static Border View() => new()
    {
        IsRenderMotionNode = true,
        Background = new SolidColorBrush(Colors.SteelBlue),
        Child = new Border { Width = 50, Height = 50, Background = new SolidColorBrush(Colors.Tomato) }
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShownThenReplaced(Stage stage)
    {
        var view = View();
        stage.Presenter.Content = view;
        stage.Frame();
        stage.Presenter.Content = View();
        return new WeakReference(view);
    }

    private static void Settle(Stage stage)
    {
        DiscardedVisuals.Drain(int.MaxValue);
        for (var i = 0; i < 3; i++) stage.Frame();
        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [Test]
    public void AViewSwappedOutOfAWindowIsCollected()
    {
        using var stage = new Stage();

        var gone = ShownThenReplaced(stage);
        stage.Frame();
        Settle(stage);

        Assert.That(gone.IsAlive, Is.False,
            "the window's cache never heard the view leave, so it kept the view and everything it drew");
    }

    [Test]
    public void AViewThatLeavesUnderAFullWalkIsCollected()
    {
        using var stage = new Stage();

        var gone = ShownThenReplaced(stage);
        RenderDirty.MarkStructural();
        stage.Frame();
        Settle(stage);

        Assert.That(gone.IsAlive, Is.False, "the full walk froze the layout of a view that had already left");
    }
}
