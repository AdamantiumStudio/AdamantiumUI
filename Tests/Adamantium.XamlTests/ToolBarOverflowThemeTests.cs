using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A tool bar's overflow menu under each theme: the commands that did not fit stand on the theme's flyout
/// surface - solid, or a tinted material - so they can be read over what is under them.</summary>
[TestFixture]
public class ToolBarOverflowThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private Theme Use(string name)
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
        return theme;
    }

    // A contextual bar given no color of its own is washed in the theme's accent, faintly, behind the whole bar.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AContextualBarWithNoColorOfItsOwn_IsWashedInTheThemesAccent(string name)
    {
        var theme = Use(name);
        var bar = new ToolBar
        {
            Header = "Table", IsContextual = true, ContextualHeader = "Table Tools", Items = { new Button { Content = "Insert row" } }
        };
        var window = new Window { Width = 300, Height = 200, Content = new ToolBarTray { Items = { bar } } };
        window.ApplyCurrentTheme();
        WindowExtension.UpdateTree(window);

        var wash = (Border)bar.GetTemplateChild("ContextualWash");

        Assert.Multiple(() =>
        {
            Assert.That(wash.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That((wash.Background as SolidColorBrush)?.Color,
                Is.EqualTo(((SolidColorBrush)theme.AccentFillColorDefault).Color));
            Assert.That(wash.Opacity, Is.GreaterThan(0).And.LessThan(1), "faint, the theme's share");
            Assert.That(((Border)bar.GetTemplateChild("ContextualHeaderPlate")).CornerRadius.TopLeft, Is.GreaterThan(0),
                "its name's plate rounded as the commands are");
            Assert.That(((Border)bar.GetTemplateChild("ContextualHeaderPlate")).CornerRadius,
                Is.EqualTo(_app.ResourceManager.FindResource("CornerRadiusDefault")));
        });
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void TheOverflowMenu_StandsOnTheThemesFlyoutSurface(string theme)
    {
        Use(theme);
        var bar = new ToolBar { Header = "Format" };
        for (var i = 0; i < 20; i++)
        {
            bar.Items.Add(new Button { Content = "Command " + i });
        }

        var window = new Window { Width = 300, Height = 200, Content = new ToolBarTray { Items = { bar } } };
        window.ApplyCurrentTheme();
        WindowExtension.UpdateTree(window);
        bar.IsOverflowOpen = true;
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(window);
            window.PopupLayer.UpdateLayout(new Adamantium.Mathematics.Size<double>(window.Width, window.Height));
        }

        var items = (IUIComponent)bar.GetTemplateChild("PART_OverflowItems");
        var surface = items.GetVisualAncestors().OfType<Border>().First();

        // A solid surface is opaque; a material - Fluent's acrylic, macOS's glass - is tinted, so the menu reads over
        // what it blurs.
        Assert.Multiple(() =>
        {
            Assert.That(surface.Background, Is.Not.Null, "a surface with no fill shows the bar under the menu through it");
            if (surface.Background is SolidColorBrush solid)
            {
                Assert.That(solid.Color.A, Is.EqualTo(255), "a see-through fill mixes the menu with the bar under it");
            }
            else
            {
                Assert.That(surface.Background, Is.InstanceOf<MaterialBrush>());
                Assert.That(((MaterialBrush)surface.Background).TintOpacity, Is.GreaterThan(0.3));
            }
        });
    }
}
