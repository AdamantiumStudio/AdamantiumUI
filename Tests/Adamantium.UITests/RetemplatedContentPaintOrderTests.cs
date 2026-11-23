using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Extensions;
using Adamantium.UI.Rendering;
using Adamantium.UITests.Rendering;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// Content handed from one template's presenter to the next one's must be drawn over its new ancestors - a docking tool
/// panel takes its own template once it learns its kind, and the pane's body came out under the panel's background.
/// </summary>
public class RetemplatedContentPaintOrderTests
{
    private sealed class Root : Grid, IRootVisualComponent
    {
        public Vector2<double> PointToClient(PixelPoint point) => new((float)point.X, (float)point.Y);
        public PixelPoint PointToScreen(Vector2<double> point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private static ControlTemplate Template(out Border[] body, Brush background)
    {
        var made = new Border[1];
        body = made;
        return new ControlTemplate(() =>
        {
            var result = new TemplateResult();
            var presenter = new ContentPresenter();
            made[0] = new Border { Background = background, Padding = new Thickness(4), Child = presenter };
            result.RegisterName("PART_ContentPresenter", presenter);
            result.AddTemplateBinding(presenter, "Content", new TemplateBinding { Path = "Content" });
            result.RootComponent = new Grid { Children = { made[0] } };
            return result;
        });
    }

    private static void Frame(Root root, RenderCache cache)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            root.Measure(new Size<double>(400, 300), force: true);
            root.Arrange(new Rect(0, 0, 400, 300));
        }

        WindowExtension.UpdateTree(root);
        cache.RecordFrame(root);
        cache.ApplyFrame();
    }

    // One record, two placements of the same leaf: its whole branch is put somewhere else, and the leaf itself is
    // re-attached inside it. The leaf used to take its parent's rank from before the branch moved - under the branch's
    // own background, as a docking panel's body did after a pane was taken out into a window.
    [Test]
    public void ALeafReattachedInsideABranchThatMoved_StaysOverTheBranch()
    {
        var leaf = new Border { Background = new SolidColorBrush(Colors.Red), Width = 20, Height = 10 };
        var middle = new Border { Background = new SolidColorBrush(Colors.Green), Padding = new Thickness(2), Child = leaf };
        var branch = new Border { Background = new SolidColorBrush(Colors.Gray), Padding = new Thickness(2), Child = middle };
        var other = new Border { Background = new SolidColorBrush(Colors.Blue), Width = 30, Height = 30 };
        var root = new Root { ClientWidth = 400, ClientHeight = 300 };
        root.Children.Add(branch);
        root.Children.Add(other);
        var cache = new RenderCache(new DrawingContext(), new FakeRenderUnitFactory());
        Frame(root, cache);

        // Later in the order, so the branch's new ranks are all above its old ones.
        root.Children.Remove(branch);
        root.Children.Add(branch);
        middle.Child = null;
        middle.Child = leaf;
        Frame(root, cache);

        Assert.Multiple(() =>
        {
            Assert.That(cache.PaintRankOf(branch), Is.GreaterThan(cache.PaintRankOf(other)), "the branch moved after the other");
            Assert.That(cache.PaintRankOf(middle), Is.GreaterThan(cache.PaintRankOf(branch)));
            Assert.That(cache.PaintRankOf(leaf), Is.GreaterThan(cache.PaintRankOf(middle)), "the leaf is drawn over its parent");
        });
    }

    // A branch carried from one panel to a later one, and then a part of it that was hidden shown: the part is drawn over
    // the branch's background - as a tool bar's checked mark, its bar dragged to a slot, came out under the bar. A hidden
    // part keeps its rank to come back in place; carried with its branch, that place is gone.
    [Test]
    public void APartShownInABranchCarriedToAnotherPanel_IsDrawnOverTheBranch()
    {
        var mark = new Border { Background = new SolidColorBrush(Colors.Red), Height = 2, Visibility = Visibility.Hidden };
        var face = new Border { Background = new SolidColorBrush(Colors.Green), Width = 20, Height = 20 };
        var button = new Grid { Children = { face, mark } };
        var bar = new Border { Background = new SolidColorBrush(Colors.Gray), Padding = new Thickness(2), Child = button };
        var first = new StackPanel { Children = { bar } };
        var second = new StackPanel();
        var root = new Root { ClientWidth = 400, ClientHeight = 300 };
        root.Children.Add(new Border { Background = new SolidColorBrush(Colors.Blue), Child = first });
        root.Children.Add(new Border { Background = new SolidColorBrush(Colors.Yellow), Child = second });
        var cache = new RenderCache(new DrawingContext(), new FakeRenderUnitFactory());
        Frame(root, cache);

        first.Children.Remove(bar);
        second.Children.Add(bar);
        Frame(root, cache);
        mark.Visibility = Visibility.Visible;
        Frame(root, cache);
        var shownBy = cache.LastBuildKind;

        Assert.Multiple(() =>
        {
            Assert.That(shownBy, Is.Not.EqualTo(RenderBuildKind.Full), "shown in place, not by a walk of the whole tree");
            Assert.That(cache.PaintRankOf(face), Is.GreaterThan(cache.PaintRankOf(bar)), "the button over the bar");
            Assert.That(cache.PaintRankOf(mark), Is.GreaterThan(cache.PaintRankOf(bar)), "the mark over the bar");
            Assert.That(cache.PaintRankOf(mark), Is.GreaterThan(cache.PaintRankOf(face)), "the mark over the button");
        });
    }

    [Test]
    public void ContentCarriedIntoANewTemplate_IsDrawnOverItsNewBackground()
    {
        var content = new Border { Background = new SolidColorBrush(Colors.Red), Width = 50, Height = 20 };
        var host = new ContentControl { Content = content, Template = Template(out _, null) };
        var root = new Root { ClientWidth = 400, ClientHeight = 300 };
        root.Children.Add(host);
        var cache = new RenderCache(new DrawingContext(), new FakeRenderUnitFactory());
        Frame(root, cache);

        host.Template = Template(out var body, new SolidColorBrush(Colors.Gray));
        Frame(root, cache);
        Frame(root, cache);

        Assert.Multiple(() =>
        {
            Assert.That(content.VisualParent?.VisualParent, Is.SameAs(body[0]), "the content moved into the new template");
            Assert.That(cache.PaintRankOf(content), Is.GreaterThan(cache.PaintRankOf(body[0])),
                "the content must be drawn after - over - the background it now sits on");
        });
    }
}
