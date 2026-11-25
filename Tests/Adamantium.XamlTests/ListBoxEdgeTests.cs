using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A list's edge is its own properties in every theme: outlined by default, and gone with
/// <c>BorderThickness="0"</c>, as a list of a window's sections wants.</summary>
[TestFixture]
public class ListBoxEdgeTests
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
        // The resources first: a theme takes the app's resource manager as it is made.
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Border EdgeOf(ListBox list)
    {
        // The window themed first, as an app's is: the list joins a tree whose theme resources are there.
        var window = new Window { Width = 400, Height = 300 };
        window.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
        window.Content = list;
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }

        return list.VisualChildren.OfType<Border>().Single();
    }

    // The edges each theme drew before the edge became the list's own properties.
    [Test]
    [TestCase("Fluent", 1, 4)]
    [TestCase("MacOs", 1, 6)]
    [TestCase("Graphite", 1, 0)]
    public void AListIsOutlinedByDefault(string theme, double thickness, double corner)
    {
        Use(theme);

        var edge = EdgeOf(new ListBox { ItemsSource = new[] { "One", "Two" } });

        Assert.Multiple(() =>
        {
            Assert.That(edge.BorderThickness, Is.EqualTo(new Thickness(thickness)));
            Assert.That(edge.CornerRadius.TopLeft, Is.EqualTo(corner));
            Assert.That(edge.BorderBrush, Is.Not.Null);
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AListWithNoEdgeShowsNone(string theme)
    {
        Use(theme);

        var edge = EdgeOf(new ListBox { ItemsSource = new[] { "One", "Two" }, BorderThickness = new Thickness(0) });

        Assert.That(edge.BorderThickness, Is.EqualTo(new Thickness(0)));
    }
}
