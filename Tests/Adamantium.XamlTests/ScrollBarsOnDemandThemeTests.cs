using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Under every theme a SCROLL BAR IS BUILT ONLY WHEN THERE IS SOMETHING TO SCROLL. Every text box and every list
/// carries a viewer, and two bars of parts each that nobody sees were a quarter of a canvas's tree.</summary>
[TestFixture]
public class ScrollBarsOnDemandThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void Use(string name)
    {
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static ScrollViewer Shown(double contentHeight)
    {
        var viewer = new ScrollViewer
        {
            Width = 200,
            Height = 200,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Width = 100, Height = contentHeight }
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

    private static ScrollBar Bar(ScrollViewer viewer, string name) => (ScrollBar)viewer.GetTemplateChild(name);

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ContentThatFits_BuildsNoBar(string theme)
    {
        Use(theme);
        var viewer = Shown(100);

        Assert.That(Bar(viewer, "PART_VerticalScrollBar").VisualChildren, Is.Empty);
        Assert.That(Bar(viewer, "PART_HorizontalScrollBar").VisualChildren, Is.Empty);
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ContentTooTall_BuildsTheVerticalBarOnly(string theme)
    {
        Use(theme);
        var viewer = Shown(900);
        var vertical = Bar(viewer, "PART_VerticalScrollBar");

        Assert.That(vertical.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(vertical.VisualChildren, Is.Not.Empty, "the bar there is something to scroll for");
        Assert.That(vertical.ActualHeight, Is.GreaterThan(0), "and it is laid out");
        Assert.That(Bar(viewer, "PART_HorizontalScrollBar").VisualChildren, Is.Empty);
    }
}
