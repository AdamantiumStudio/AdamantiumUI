using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Adorners;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>Where a key tip's badge hangs in every theme: below a command's icon, as Office hangs it.</summary>
[TestFixture]
public class KeyTipBadgeThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    // The resource manager FIRST: a theme resolves its resource references while it is built, against the manager in
    // force then - built before the swap, it reads the previous test's resources, and the first theme reads none.
    private void Use(string name)
    {
        _app.ResourceManager = new ResourceManager();
        Theme theme = name switch
        {
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };

        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    // A small command with its label beside the icon wore the badge under the middle of the whole command - under the
    // label, away from what the eye finds the command by.
    [TestCase("Fluent", RibbonSize.Large)]
    [TestCase("Fluent", RibbonSize.Medium)]
    [TestCase("Graphite", RibbonSize.Medium)]
    [TestCase("MacOs", RibbonSize.Medium)]
    public void ABadge_HangsBelowTheCommandsIcon(string theme, RibbonSize size)
    {
        Use(theme);
        var command = new RibbonButton { Content = "Format painter", Icon = new Border { Width = 16, Height = 16 } };
        Ribbon.SetSize(command, size);
        command.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(command);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        command.Measure(new Size<double>(double.PositiveInfinity, 100));
        command.Arrange(new Rect(command.DesiredSize));
        var icon = ((IUIComponent)command.GetTemplateChild("PART_Icon")).TransformBoundsToVisual(command);

        var badge = new KeyTipAdorner(command, "FP", KeyTipPlacement.Bottom).PlaceIn(new Size<double>(18, 16));

        Assert.Multiple(() =>
        {
            Assert.That(badge.X + badge.Width / 2, Is.EqualTo(icon.X + icon.Width / 2).Within(0.5), "centered on the icon");
            Assert.That(badge.Y + badge.Height / 2, Is.EqualTo(icon.Bottom).Within(0.5), "astride its lower edge");
        });
    }
}
