using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A drop-down button and a split button down the side of a bar, under each theme: the arrow under the content -
/// a split's own part under its body - and the list opening beside the column; along a bar, as they were.</summary>
[TestFixture]
public class DropDownButtonDownTheSideThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    // The resources first: a theme's styles look theirs up as they are built.
    private void Use(string name)
    {
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

    private static void Show(Window window)
    {
        window.ApplyCurrentTheme();
        for (var i = 0; i < 4; i++)
        {
            WindowExtension.UpdateTree(window);
        }
    }

    private static Vector2<double> Middle(IUIComponent element, IUIComponent within) =>
        element.TranslatePoint(new Vector2<double>(element.RenderSize.Width / 2, element.RenderSize.Height / 2), within);

    private static Vector2<double> Corner(IUIComponent element, IUIComponent within) =>
        element.TranslatePoint(new Vector2<double>(0, 0), within);

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void DownTheSide_TheArrowStandsUnderTheContent_AndTheListOpensBesideTheColumn(string theme)
    {
        Use(theme);
        var drop = new DropDownButton { Content = "W" };
        var split = new SplitButton { Content = "R" };
        var group = new ToolBarGroup { Header = "Arrange", Items = { drop, split } };
        var tray = new ToolBarTray { Orientation = Orientation.Vertical, Items = { new ToolBar { Header = "Picture", Items = { group } } } };
        var window = new Window
        {
            Width = 600, Height = 600,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { tray } }
        };
        Show(window);

        var dropContent = (IUIComponent)drop.GetTemplateChild("PART_ContentPresenter");
        var dropArrow = (IUIComponent)drop.GetTemplateChild("Chevron");
        var body = (IUIComponent)split.GetTemplateChild("Body");
        var arrowPart = (IUIComponent)split.GetTemplateChild("PART_DropDownArea");

        Assert.Multiple(() =>
        {
            Assert.That(Corner(dropArrow, drop).Y, Is.GreaterThanOrEqualTo(Corner(dropContent, drop).Y + dropContent.RenderSize.Height - 0.5),
                "the arrow under the content");
            Assert.That(Middle(dropArrow, drop).X, Is.EqualTo(Middle(dropContent, drop).X).Within(1), "centered under it");
            Assert.That(Corner(arrowPart, split).Y, Is.GreaterThanOrEqualTo(Corner(body, split).Y + body.RenderSize.Height - 0.5),
                "the split's arrow part under its body");
            Assert.That(arrowPart.RenderSize.Width, Is.EqualTo(body.RenderSize.Width).Within(0.5), "as wide as the body");
            Assert.That(((Popup)drop.GetTemplateChild("PART_Popup")).Placement, Is.EqualTo(PlacementMode.Right));
            Assert.That(((Popup)split.GetTemplateChild("PART_Popup")).Placement, Is.EqualTo(PlacementMode.Right));
        });

        tray.Orientation = Orientation.Horizontal;
        Show(window);

        Assert.Multiple(() =>
        {
            Assert.That(Middle(dropArrow, drop).X, Is.GreaterThan(Middle(dropContent, drop).X), "along, after the content");
            Assert.That(Middle(arrowPart, split).X, Is.GreaterThan(Middle(body, split).X), "along, after the body");
            Assert.That(((Popup)drop.GetTemplateChild("PART_Popup")).Placement, Is.EqualTo(PlacementMode.Bottom));
        });
    }
}
