using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.GraphiteTheme;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>An overlay window collapses to its title bar by its own button, in each theme, and its smallest size is its
/// MinWidth and MinHeight.</summary>
[TestFixture]
public class OverlayWindowCollapseTests
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
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        Theme theme = name switch
        {
            "Fluent" => new Fluent(),
            "Graphite" => new Graphite(),
            _ => new MacOs()
        };
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static OverlayWindow Built(bool canCollapse)
    {
        var window = new OverlayWindow
        {
            Title = "probe",
            CanCollapse = canCollapse,
            CanResize = true,
            Height = 240,
            Content = new TextBlock { Text = "content" }
        };
        Settle(window);
        return window;
    }

    private static void Settle(OverlayWindow window)
    {
        window.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        // As its popup lays it out: at the size it asks for.
        window.Measure(new Size<double>(600, 400));
        window.Arrange(new Rect(0, 0, window.DesiredSize.Width, window.DesiredSize.Height));
    }

    // The macOS caption hides the light's cell, glyph and all; the others hide the button itself.
    private static UIComponent CollapseControl(OverlayWindow window, string theme) =>
        window.GetTemplateChild(theme == "MacOs" ? "CollapseCell" : "PART_CollapseButton") as UIComponent;

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void TheCollapseButtonShowsOnlyWithCanCollapse(string theme)
    {
        Use(theme);

        Assert.Multiple(() =>
        {
            Assert.That(CollapseControl(Built(canCollapse: false), theme)?.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(CollapseControl(Built(canCollapse: true), theme)?.Visibility, Is.EqualTo(Visibility.Visible));
        });
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ItsButtonCollapsesTheWindowToItsTitleBarAndBack(string theme)
    {
        Use(theme);
        var window = Built(canCollapse: true);
        var button = (Button)window.GetTemplateChild("PART_CollapseButton");
        var content = (UIComponent)window.GetTemplateChild("PART_ContentPresenter");
        var grip = (UIComponent)window.GetTemplateChild("PART_ResizeGrip");
        var titleBar = (MeasurableUIComponent)window.GetTemplateChild("PART_TitleBar");

        button.PerformClick();
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(window.IsCollapsed, Is.True);
            Assert.That(content.Visibility, Is.EqualTo(Visibility.Collapsed), "the content goes");
            Assert.That(grip.Visibility, Is.EqualTo(Visibility.Collapsed), "and the grip with it");
            Assert.That(window.DesiredSize.Height, Is.LessThan(titleBar.DesiredSize.Height + 4),
                "the window is as tall as its title bar, not its MinHeight or the Height it had");
        });

        button.PerformClick();
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(window.IsCollapsed, Is.False);
            Assert.That(content.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(grip.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(window.Height, Is.EqualTo(240), "expanded, it has the height it had");
            Assert.That(window.MinHeight, Is.EqualTo(120), "and its smallest height again");
        });
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void CollapsedItKeepsTheWidthItWasShownAt(string theme)
    {
        Use(theme);
        var window = new OverlayWindow
        {
            Title = "probe",
            CanCollapse = true,
            Content = new TextBlock { Text = "A content much wider than the smallest width an overlay window has" }
        };
        Settle(window);
        var shown = window.DesiredSize.Width;

        window.IsCollapsed = true;
        Settle(window);
        var collapsed = window.DesiredSize.Width;
        window.IsCollapsed = false;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.GreaterThan(window.MinWidth), "the content is wider than the smallest width");
            Assert.That(collapsed, Is.EqualTo(shown), "collapsed, the title bar does not narrow it");
            Assert.That(window.Width, Is.NaN, "expanded, it is sized to its content again");
        });
    }

    [TestCase("Fluent", 300)]
    [TestCase("Graphite", 260)]
    [TestCase("MacOs", 300)]
    public void TheSmallestWidthIsTheWindowsMinWidth(string theme, double smallest)
    {
        Use(theme);
        var window = Built(canCollapse: false);
        var narrow = new OverlayWindow { Title = "probe", Content = new TextBlock { Text = "x" } };
        Settle(narrow);

        Assert.Multiple(() =>
        {
            Assert.That(window.MinWidth, Is.EqualTo(smallest), "the theme's smallest width is the window's own");
            Assert.That(narrow.DesiredSize.Width, Is.EqualTo(smallest), "and a narrow content is laid out to it");
        });
    }
}
