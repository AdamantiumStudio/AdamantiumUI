using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>SEVERAL THINGS AT ONCE are placed by the frame round them. A line per item, read again whenever any of them
/// moved, made a drag of a thousand strokes take a second a step; one line for the frame costs the same for one item
/// and for thousands.</summary>
[TestFixture]
public class CanvasSelectionFrameTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private InfiniteCanvas Built(out List<ICanvasItem> strokes, bool shapes = false)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);

        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        var theme = new Adamantium.UI.Themes.FluentTheme.Fluent();

        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);

        var scene = new CanvasScene();
        var canvas = new InfiniteCanvas { Width = 900, Height = 600, Scene = scene };
        canvas.ApplyCurrentTheme();

        strokes = [];
        for (var i = 0; i < 3; i++)
        {
            ICanvasItem item;
            if (shapes)
            {
                item = new ShapeItem(CanvasShape.Rectangle, new Rect(i * 100, 0, 40, 20), Brushes.White, 1);
            }
            else
            {
                var stroke = new StrokeItem(new Vector2<double>(i * 100, 0), Brushes.White, 2);
                stroke.Add(new Vector2<double>(i * 100, 0));
                stroke.Add(new Vector2<double>(i * 100 + 40, 20));
                item = stroke;
            }

            scene.Add(item);
            strokes.Add(item);
        }

        Settle(canvas);
        return canvas;
    }

    private static void Settle(InfiniteCanvas canvas)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(canvas);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
            canvas.Measure(new Size<double>(900, 600), force: true);
            canvas.Arrange(new Rect(0, 0, 900, 600));
        }
    }

    private static IEnumerable<PropertyRow> Rows(PropertyGrid grid)
    {
        foreach (var section in grid.Displayed)
        {
            if (section.Content is not IUIComponent host)
            {
                continue;
            }

            foreach (var child in host.VisualChildren)
            {
                if (child is PropertyRow row)
                {
                    yield return row;
                }
            }
        }
    }

    [Test]
    public void SeveralSelectedArePlacedByTheFrame()
    {
        var canvas = Built(out var strokes);

        canvas.SelectMany(strokes, false);
        Settle(canvas);

        var grid = canvas.Inspector;
        Assert.That(grid, Is.Not.Null, "the canvas has no inspector");

        var framed = grid.Displayed.Where(CanvasInspector.GetReadsSelectionFrame).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(framed, Has.Count.EqualTo(1), "no section reads the frame");
            Assert.That(framed.FirstOrDefault()?.Target, Is.SameAs(canvas.SelectionFrame), "the section reads something else");
            Assert.That(Rows(grid).Where(r => r.Definition is CompositeProperty && r.Targets.Count > 1), Is.Empty,
                "a place or a size is still read item by item");
        });
    }

    [Test]
    public void OneSelectedIsPlacedByItself()
    {
        var canvas = Built(out var strokes);

        canvas.Select(strokes[0], false);
        Settle(canvas);

        Assert.That(canvas.Inspector.Displayed.Where(CanvasInspector.GetReadsSelectionFrame), Is.Empty,
            "one thing selected is shown its frame instead of itself");
    }

    // ...AND GETS THEM BACK after several were selected. The lines' choice was asked of the panel while the rows were
    // being rebuilt out of its tree, came back with nothing, and stayed that way.
    [Test]
    public void OneSelectedAfterSeveralIsPlacedByItselfAgain()
    {
        var canvas = Built(out var strokes);

        canvas.Select(strokes[0], false);
        Settle(canvas);
        var alone = Rows(canvas.Inspector).Count(r => r.Definition is CompositeProperty);

        canvas.SelectMany(strokes, false);
        Settle(canvas);
        canvas.Select(strokes[0], false);
        Settle(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(alone, Is.GreaterThan(0), "one thing selected had no place and size lines to begin with");
            Assert.That(Rows(canvas.Inspector).Count(r => r.Definition is CompositeProperty), Is.EqualTo(alone),
                "after several were selected, the one selected again lost its place and size");
        });
    }

    [Test]
    public void TheFrameFollowsEverythingMoved()
    {
        var canvas = Built(out var strokes);
        canvas.SelectMany(strokes, false);

        var frame = canvas.SelectionFrame;
        var was = frame.X;
        var heard = new List<string>();
        frame.PropertyChanged += (_, e) => heard.Add(e.PropertyName);

        foreach (var item in canvas.Selection) item.Move(new Vector2<double>(25, 0));
        canvas.Scene.Touch();

        Assert.Multiple(() =>
        {
            Assert.That(frame.X, Is.EqualTo(was + 25).Within(1e-6));
            Assert.That(heard.Count(name => name == "X"), Is.EqualTo(1), "once per edit, however many moved");
            Assert.That(heard, Does.Not.Contain("Width"), "a move said the size changed");
        });
    }

    [Test]
    public void WritingTheFrameMovesEverythingAsOneStep()
    {
        var canvas = Built(out var strokes);
        canvas.SelectMany(strokes, false);

        var before = strokes.Select(s => s.Bounds.X).ToList();

        canvas.SelectionFrame.X += 50;

        Assert.That(strokes.Select(s => s.Bounds.X), Is.EqualTo(before.Select(x => x + 50)).Within(1e-6),
            "not everything in the frame moved with it");

        canvas.Undo();

        Assert.That(strokes.Select(s => s.Bounds.X), Is.EqualTo(before).Within(1e-6), "one undo did not take it back");
    }

    [Test]
    public void WideningTheFrameScalesEverythingInIt()
    {
        var canvas = Built(out var strokes, shapes: true);
        canvas.SelectMany(strokes, false);

        var frame = canvas.SelectionFrame;
        var left = frame.X;
        var gaps = strokes.Select(s => s.Bounds.X - left).ToList();

        frame.Width *= 2;

        Assert.Multiple(() =>
        {
            Assert.That(frame.X, Is.EqualTo(left).Within(1e-6), "the frame's left edge moved");
            Assert.That(strokes.Select(s => s.Bounds.X - left), Is.EqualTo(gaps.Select(g => g * 2)).Within(1e-3),
                "the things in it did not keep their places in it");
        });
    }
}
