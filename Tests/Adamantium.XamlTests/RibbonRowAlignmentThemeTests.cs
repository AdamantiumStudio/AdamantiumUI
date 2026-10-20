using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Every theme gives a group's commands the group's height, so spread rows reach its bottom.</summary>
[TestFixture]
public class RibbonRowAlignmentThemeTests
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

    private static RibbonButton Small(string content)
    {
        var button = new RibbonButton { Content = content };
        Ribbon.SetMaxSize(button, RibbonSize.Medium);
        return button;
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void SpreadRows_ReachTheBottomOfTheGroupsRoom(string theme)
    {
        Use(theme);
        var forward = Small("Forward");
        var backward = Small("Backward");
        var group = new RibbonGroup { Header = "Order", Items = { forward, backward } };
        Ribbon.SetRowAlignment(group, RibbonRowAlignment.Spread);

        group.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(group);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        group.Measure(new Size(400, 120));
        group.Arrange(new Rect(0, 0, 400, 120));

        var panel = group.GetVisualDescendants().OfType<RibbonGroupPanel>().Single();
        var rows = forward.DesiredSize.Height + backward.DesiredSize.Height + panel.RowSpacing;
        Assert.Multiple(() =>
        {
            Assert.That(panel.Bounds.Height, Is.GreaterThan(rows), "the panel has the group's room, not its rows'");
            Assert.That(backward.Bounds.Bottom + backward.Margin.Bottom, Is.EqualTo(panel.Bounds.Height).Within(0.5),
                "the last row at the bottom");
        });
    }
}
