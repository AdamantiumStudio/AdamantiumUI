using System.Collections.Generic;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.GraphiteTheme;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A context menu's card is a surface of its own, so its words take the card's color, not the color of what the menu
/// belongs to. A row's color is a bare-type style, which inheritance outranks, and the caption's overflow menu took the
/// caption's white: white words on the light theme's white card.
/// </summary>
[TestFixture]
public class ContextMenuCardForegroundThemeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    private void UseLight(string name)
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
        themes.SetVariant(ThemeVariant.Light);
    }

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            window.PopupLayer.UpdateLayout(new Size(window.Width, window.Height));
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static Color RowColorUnder(Brush ownerForeground)
    {
        var menu = new ContextMenu { ItemsSource = new List<object> { "Ribbon" } };
        var owner = new Border { Child = menu };
        if (ownerForeground != null)
        {
            owner.Foreground = ownerForeground;
        }

        var window = new Window { Width = 400, Height = 300, Content = owner };
        Settle(window);
        menu.IsOpen = true;
        Settle(window);

        var row = menu.ItemContainerGenerator.ContainerFromIndex(0) as MenuItem;
        Assert.That(row, Is.Not.Null, "the opened menu made its row");
        return (row.Foreground as SolidColorBrush)?.Color ?? default;
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ARowTakesItsCardsColor_NotItsOwners(string theme)
    {
        UseLight(theme);
        var onItsOwn = RowColorUnder(null);
        var underWhiteWords = RowColorUnder(new SolidColorBrush(Colors.White));

        Assert.Multiple(() =>
        {
            Assert.That(onItsOwn, Is.Not.EqualTo(Colors.White), "the light theme's words are dark");
            Assert.That(underWhiteWords, Is.EqualTo(onItsOwn), "the owner's white reached the card");
        });
    }
}
