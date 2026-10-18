using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A segmented control in every theme is as tall as one control and each segment as wide as its own words,
/// whatever room it is given.</summary>
[TestFixture]
public class SegmentedControlLayoutTests
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

    private static Theme ThemeNamed(string name) => name switch
    {
        "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
        "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
        _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
    };

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void InATallRow_TheControlKeepsItsHeight_AndEachSegmentItsWidth(string theme)
    {
        Use(ThemeNamed(theme));
        var segments = new SegmentedControl();
        var shaded = new SegmentedControlItem { Content = "Shaded" };
        var both = new SegmentedControlItem { Content = "Shaded and wireframe, both" };
        segments.Items.Add(shaded);
        segments.Items.Add(both);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        row.Children.Add(segments);
        var host = new Grid { Width = 900, Height = 600 };
        host.Children.Add(row);

        host.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(host);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        host.Measure(new Size(900, 600));
        host.Arrange(new Rect(0, 0, 900, 600));

        Assert.Multiple(() =>
        {
            Assert.That(segments.ActualHeight, Is.LessThan(60));
            Assert.That(row.ActualHeight, Is.LessThan(60));
            Assert.That(both.ActualWidth, Is.GreaterThan(shaded.ActualWidth * 2));
        });
    }
}
