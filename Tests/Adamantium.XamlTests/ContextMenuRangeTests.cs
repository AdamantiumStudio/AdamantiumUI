using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A menu that selects a range, as an undo list does: the row reached marks every row from the first to it,
/// the menu says how many, and the line under the rows says it in the words given; opened again, nothing is reached.
/// </summary>
[TestFixture]
public class ContextMenuRangeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void TheRowReached_MarksEveryRowAboveIt_AndTheLineSaysHowMany(string theme)
    {
        Use(theme);
        var menu = new ContextMenu
        {
            ItemsSource = new List<object> { "Move", "Rotate", "Scale", "Delete" },
            SelectsRange = true,
            RangeFormat = "Undo {0}"
        };
        var window = Opened(menu);
        var rows = Enumerable.Range(0, 4).Select(index => (MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(index))
            .ToList();

        rows[2].Focus();
        Settle(window);
        var line = RangeLineOf(menu);

        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(row => row.IsInRange), Is.EqualTo(new[] { true, true, true, false }));
            Assert.That(menu.RangeCount, Is.EqualTo(3));
            Assert.That(line.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(line.Text, Is.EqualTo("Undo 3"));
        });
    }

    [Test]
    public void OpenedAgain_TheMenuReachesNothing()
    {
        Use("Fluent");
        var menu = new ContextMenu
        {
            ItemsSource = new List<object> { "Move", "Rotate" },
            SelectsRange = true,
            RangeFormat = "Undo {0}"
        };
        var window = Opened(menu);
        ((MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(1)).Focus();
        Settle(window);
        menu.IsOpen = false;
        Settle(window);

        menu.IsOpen = true;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(menu.RangeCount, Is.Zero);
            Assert.That(RangeLineOf(menu).Visibility, Is.EqualTo(Visibility.Collapsed));
        });
    }

    [Test]
    public void AMenuThatSelectsNoRange_MarksNoRow()
    {
        Use("Fluent");
        var menu = new ContextMenu { ItemsSource = new List<object> { "Cut", "Copy" } };
        var window = Opened(menu);
        var copy = (MenuItem)menu.ItemContainerGenerator.ContainerFromIndex(1);

        copy.Focus();
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(copy.IsInRange, Is.False);
            Assert.That(menu.RangeCount, Is.Zero);
        });
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
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static Window Opened(ContextMenu menu)
    {
        var owner = new Border { Child = menu };
        var window = new Window { Width = 600, Height = 400, Content = owner };
        Settle(window);
        menu.IsOpen = true;
        Settle(window);
        return window;
    }

    private static TextBlock RangeLineOf(ContextMenu menu) =>
        (TextBlock)((Popup)menu.GetTemplateChild("PART_Popup")).FindContentChild("PART_RangeLine");

    private static void Settle(Window window)
    {
        for (var i = 0; i < 3; i++)
        {
            window.ApplyCurrentTheme();
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            window.PopupLayer.UpdateLayout(new Size<double>(window.Width, window.Height));
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }
}
