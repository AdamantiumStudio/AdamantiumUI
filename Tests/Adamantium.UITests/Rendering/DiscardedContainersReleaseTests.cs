using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Graphics;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Media.Drawings;
using Adamantium.UI.Core.Media.Imaging;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using Adamantium.UI.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests.Rendering;

/// <summary>Containers a list drops - its items reset, or the whole list closed - must be collectable.</summary>
[TestFixture]
[Category("Gpu")]
public class DiscardedContainersReleaseTests
{
    private const int Width = 200;
    private const int Height = 300;

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

    private static string[] Items()
    {
        var items = new string[200];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = "item " + i;
        }

        return items;
    }

    public enum Look
    {
        Plain,
        Icon,
        StrokedIcon
    }

    private static DrawingImage Icon(Look look) => look == Look.Plain
        ? null
        : new DrawingImage
        {
            Drawing = new GeometryDrawing
            {
                Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)),
                Brush = new SolidColorBrush(Colors.White),
                Pen = look == Look.StrokedIcon ? new Pen(new SolidColorBrush(Colors.Red), 2) : null
            }
        };

    private static ItemsControl List(DrawingImage icon) => new()
    {
        ItemsSource = Items(),
        ItemTemplate = new DataTemplate(() => new TemplateResult
        {
            RootComponent = new Border
            {
                Height = 20,
                Background = new SolidColorBrush(Colors.SteelBlue),
                Child = icon != null ? new Image { Source = icon, Width = 16, Height = 16 } : null
            }
        }),
        Template = new ControlTemplate(() =>
        {
            var presenter = new ItemsPresenter();
            var result = new TemplateResult { RootComponent = presenter };
            result.RegisterName("PART_ItemsPresenter", presenter);
            return result;
        })
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> Shown(Stage stage, ItemsControl list)
    {
        stage.Presenter.Content = list;
        stage.Frame();
        stage.Frame();
        var realized = new List<WeakReference>();
        foreach (var index in list.ItemContainerGenerator.RealizedIndices)
        {
            realized.Add(new WeakReference(list.ItemContainerGenerator.ContainerFromIndex(index)));
        }

        Assert.That(realized, Is.Not.Empty, "the list must realize containers for the test to mean anything");
        return realized;
    }

    private static void Settle(Stage stage)
    {
        for (var i = 0; i < 8; i++)
        {
            stage.Frame();
            DiscardedVisuals.Drain(int.MaxValue);
        }

        for (var i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    private static int Alive(List<WeakReference> refs)
    {
        var alive = 0;
        foreach (var r in refs)
        {
            if (r.IsAlive)
            {
                alive++;
            }
        }

        return alive;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> ShownThenReset(Stage stage, DrawingImage icon)
    {
        var list = List(icon);
        var realized = Shown(stage, list);
        list.ItemsSource = Items();
        return realized;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> ShownThenClosed(Stage stage, DrawingImage icon)
    {
        var realized = Shown(stage, List(icon));
        stage.Presenter.Content = new Border();
        return realized;
    }

    [TestCase(Look.Plain, TestName = "ContainersOfAResetList_AreCollected")]
    [TestCase(Look.Icon, TestName = "ContainersOfAResetList_ShowingASharedIcon_AreCollected")]
    [TestCase(Look.StrokedIcon, TestName = "ContainersOfAResetList_ShowingAStrokedSharedIcon_AreCollected")]
    public void ContainersOfAResetList_AreCollected(Look look)
    {
        using var stage = new Stage();

        var icon = Icon(look);
        var realized = ShownThenReset(stage, icon);
        Settle(stage);

        Assert.That(Alive(realized), Is.Zero, $"{Alive(realized)} of {realized.Count} containers survived a reset");
        GC.KeepAlive(icon);
    }

    [TestCase(Look.Plain, TestName = "ContainersOfAClosedList_AreCollected")]
    [TestCase(Look.Icon, TestName = "ContainersOfAClosedList_ShowingASharedIcon_AreCollected")]
    [TestCase(Look.StrokedIcon, TestName = "ContainersOfAClosedList_ShowingAStrokedSharedIcon_AreCollected")]
    public void ContainersOfAClosedList_AreCollected(Look look)
    {
        using var stage = new Stage();

        var icon = Icon(look);
        var realized = ShownThenClosed(stage, icon);
        Settle(stage);

        Assert.That(Alive(realized), Is.Zero, $"{Alive(realized)} of {realized.Count} containers survived the list");
        GC.KeepAlive(icon);
    }
}
