using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Behaviors;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UITests;

/// <summary>
/// A theme swap re-templates a host whose presenter built a view, and the view itself; the view's markup content must be
/// discarded with it. It was left alive and out of the tree, so a behavior on it never detached - the sandbox's 3D view
/// kept one universe per swap running for good.
/// </summary>
[TestFixture]
public class RetemplatedHostDiscardsViewContentTests
{
    private sealed class TestWindowRoot : Grid, IRootVisualComponent
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

    private sealed class CountingBehavior : Behavior<Grid>
    {
        public int Detached { get; private set; }

        protected override void OnDetached(Grid component) => Detached++;
    }

    private static ControlTemplate PresenterTemplate() => new(() =>
    {
        var presenter = new ContentPresenter();
        var result = new TemplateResult { RootComponent = presenter };
        result.AddTemplateBinding(presenter, "Content", new TemplateBinding { Path = "Content" });
        result.AddTemplateBinding(presenter, "ContentTemplate", new TemplateBinding { Path = "ContentTemplate" });
        return result;
    });

    private static void Settle(TestWindowRoot root)
    {
        root.Measure(new Size<double>(400, 300), force: true);
        root.Arrange(new Rect(0, 0, 400, 300));
    }

    [TestCase(true, TestName = "ViewRetemplatedFirst")]
    [TestCase(false, TestName = "HostRetemplatedFirst")]
    public void AHostAndItsViewRetemplated_DiscardTheViewsContent(bool viewFirst)
    {
        Grid body = null;
        CountingBehavior behavior = null;
        ContentControl view = null;

        var host = new ContentControl
        {
            Template = PresenterTemplate(),
            Content = new object(),
            ContentTemplate = new DataTemplate(() =>
            {
                behavior = new CountingBehavior();
                body = new Grid();
                body.Behaviors.Add(behavior);
                view = new ContentControl { Template = PresenterTemplate(), Content = body };
                return new TemplateResult { RootComponent = view };
            })
        };

        var root = new TestWindowRoot { ClientWidth = 400, ClientHeight = 300 };
        root.Children.Add(host);
        Settle(root);
        Assume.That(body.RootVisual, Is.SameAs(root), "the view's content is shown to begin with");

        // The host's new presenter builds a new view; these are the old one's.
        var oldBody = body;
        var oldBehavior = behavior;

        if (viewFirst)
        {
            view.Template = PresenterTemplate();
            host.Template = PresenterTemplate();
        }
        else
        {
            host.Template = PresenterTemplate();
            view.Template = PresenterTemplate();
        }

        Settle(root);
        while (DiscardedVisuals.Drain(64) > 0) { }

        Assert.Multiple(() =>
        {
            Assert.That(oldBody.IsDiscarded, Is.True, "the old view's content outlived it, alive and out of the tree");
            Assert.That(oldBehavior.Detached, Is.EqualTo(1), "its behavior never learned it was gone");
        });
    }
}
