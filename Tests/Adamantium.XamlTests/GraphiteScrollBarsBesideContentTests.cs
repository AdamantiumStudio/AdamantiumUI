using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Graphite's scroll bars are always on, so they do not lie over the content: while a bar shows, the content
/// ends where the bar begins - a list row's right edge is never under it. Fluent's, which fade out, still lie over it.
/// </summary>
[TestFixture]
public class GraphiteScrollBarsBesideContentTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(Theme theme)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static ScrollViewer Shown(double contentWidth, double contentHeight)
    {
        var viewer = new ScrollViewer
        {
            Width = 200,
            Height = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = contentWidth, Height = contentHeight }
        };
        var window = new Window { Width = 800, Height = 600, Content = viewer };
        window.ApplyCurrentTheme();
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }

        return viewer;
    }

    private static IUIComponent Part(ScrollViewer viewer, string name) => (IUIComponent)viewer.GetTemplateChild(name);

    [Test]
    public void InGraphite_TheVerticalBarTakesAStripBesideTheContent()
    {
        Use(new Adamantium.UI.Themes.GraphiteTheme.Graphite());
        var viewer = Shown(100, 900);
        var bar = Part(viewer, "PART_VerticalScrollBar");
        var content = Part(viewer, "PART_ScrollContentPresenter");

        Assert.Multiple(() =>
        {
            Assert.That(bar.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(bar.RenderSize.Width, Is.GreaterThan(0));
            Assert.That(content.RenderSize.Width, Is.EqualTo(200 - bar.RenderSize.Width).Within(0.5),
                "the content ends at the bar");
            Assert.That(content.RenderSize.Height, Is.EqualTo(200).Within(0.5), "with no other bar, its whole height");
        });
    }

    [Test]
    public void InGraphite_BothBarsTakeTheirStrips_AndTheCornerIsBetweenThem()
    {
        Use(new Adamantium.UI.Themes.GraphiteTheme.Graphite());
        var viewer = Shown(900, 900);
        var vertical = Part(viewer, "PART_VerticalScrollBar");
        var horizontal = Part(viewer, "PART_HorizontalScrollBar");
        var content = Part(viewer, "PART_ScrollContentPresenter");
        var corner = Part(viewer, "ScrollBarCorner");

        Assert.Multiple(() =>
        {
            Assert.That(content.RenderSize.Width, Is.EqualTo(200 - vertical.RenderSize.Width).Within(0.5));
            Assert.That(content.RenderSize.Height, Is.EqualTo(200 - horizontal.RenderSize.Height).Within(0.5));
            Assert.That(vertical.RenderSize.Height, Is.EqualTo(content.RenderSize.Height).Within(0.5),
                "the corner ends the bar");
            Assert.That(corner.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [Test]
    public void InGraphite_ContentThatFits_HasTheWholeViewer()
    {
        Use(new Adamantium.UI.Themes.GraphiteTheme.Graphite());
        var content = Part(Shown(100, 100), "PART_ScrollContentPresenter");

        Assert.That(content.RenderSize.Width, Is.EqualTo(200).Within(0.5), "no bar, no strip");
    }

    [Test]
    public void InFluent_TheBarStillLiesOverTheContent()
    {
        Use(new Adamantium.UI.Themes.FluentTheme.Fluent());
        var content = Part(Shown(100, 900), "PART_ScrollContentPresenter");

        Assert.That(content.RenderSize.Width, Is.EqualTo(200).Within(0.5), "Fluent's bar fades out: it takes no strip");
    }
}
