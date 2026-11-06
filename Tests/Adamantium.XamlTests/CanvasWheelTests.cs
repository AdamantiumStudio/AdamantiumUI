using System.Collections.Generic;
using Adamantium.Core.Collections;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Canvases;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using Adamantium.UITests.Graph;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The wheel over a thing standing on the plane: the camera's, unless that thing scrolls.</summary>
[TestFixture]
public class CanvasWheelTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void UseFluent()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);

        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 6; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static bool Wheel(IUIComponent over, int delta)
    {
        var args = new PointerWheelEventArgs(Mouse.Pointer, InputModifiers.None, delta, 0)
        {
            RoutedEvent = PointerEvents.PointerWheelChangedEvent
        };
        ((IInputComponent)over).RaiseEvent(args);
        return args.Handled;
    }

    [Test]
    public void TheWheelOverANodeZooms()
    {
        var canvas = new NodeGraph { Scene = new CanvasScene() };

        var nodes = new TrackingCollection<ICanvasNode>();
        canvas.Nodes = nodes;

        var window = new Window { Width = 800, Height = 600, Content = canvas };
        Settle(window);

        var node = new GraphNode { Kind = "source", Title = "Source", Left = 40, Top = 60, Width = 160 };
        nodes.Add(node);
        Settle(window);

        IUIComponent control = null;
        foreach (var item in new List<ICanvasItem>(canvas.ItemsHere()))
        {
            if (item is ElementItem { Model: { } model } element && ReferenceEquals(model, node)) control = element.Element;
        }

        Assert.That(control, Is.Not.Null, "the node never reached the plane");

        var before = canvas.Scale;

        Assert.That(Wheel(control, 120), Is.True, "the canvas took the wheel");

        Glide();
        Assert.That(canvas.Scale, Is.GreaterThan(before), "and zoomed in");
    }

    private static void Glide()
    {
        for (var i = 0; i < 60; i++) Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(1 / 60.0);
    }

    private static (DrawingBoard Canvas, ScrollViewer Scroller, StackPanel Rows) Listed(int count)
    {
        var canvas = new DrawingBoard { Scene = new CanvasScene() };
        var window = new Window { Width = 800, Height = 600, Content = canvas };
        Settle(window);

        var rows = new StackPanel();
        for (var i = 0; i < count; i++) rows.Children.Add(new TextBlock { Text = $"row {i}" });

        var scroller = new ScrollViewer { Content = rows };
        ((CanvasScene)canvas.Scene).Add(new ElementItem(scroller, new Rect(40, 40, 200, 120)));
        Settle(window);

        return (canvas, scroller, rows);
    }

    [Test]
    public void AScrollerAtItsEndKeepsTheWheel()
    {
        var (canvas, _, rows) = Listed(40);
        var before = canvas.Scale;

        // At its top already: a notch up scrolls nothing, and is still not the camera's.
        Assert.That(Wheel(rows.Children[0], 120), Is.False, "the scroller had nowhere to go");

        Glide();
        Assert.That(canvas.Scale, Is.EqualTo(before), "and the camera stayed put");
    }

    [Test]
    public void TheScrollBarOfAScrollerAtItsEndKeepsTheWheel()
    {
        var (canvas, scroller, _) = Listed(40);
        var bar = Found<ScrollBar>(scroller);

        Assert.That(bar, Is.Not.Null, "the scroller has no bar to turn the wheel over");

        var before = canvas.Scale;
        Wheel(bar, 120);

        Glide();
        Assert.That(canvas.Scale, Is.EqualTo(before));
    }

    [Test]
    public void AScrollerWithNothingToScrollLetsTheCameraZoom()
    {
        var (canvas, _, rows) = Listed(1);
        var before = canvas.Scale;

        Assert.That(Wheel(rows.Children[0], 120), Is.True);

        Glide();
        Assert.That(canvas.Scale, Is.GreaterThan(before));
    }

    [Test]
    public void ACanvasOnAScrollingPageZoomsOverItsPlane()
    {
        var canvas = new DrawingBoard { Scene = new CanvasScene(), Height = 600 };
        var page = new StackPanel();
        page.Children.Add(canvas);
        page.Children.Add(new Border { Height = 900 });

        var window = new Window { Width = 800, Height = 600, Content = new ScrollViewer { Content = page } };
        Settle(window);

        var before = canvas.Scale;

        Assert.That(Wheel(canvas, 120), Is.True, "the canvas took the wheel, not the page");

        Glide();
        Assert.That(canvas.Scale, Is.GreaterThan(before));
    }

    private static T Found<T>(IUIComponent within) where T : class
    {
        if (within is T wanted) return wanted;

        foreach (var child in within.VisualChildren)
        {
            if (child is IUIComponent visual && Found<T>(visual) is { } deep) return deep;
        }

        return null;
    }
}
