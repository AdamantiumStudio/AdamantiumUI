using System.Collections.Generic;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A menu's rows keep a check column where a row can be checked and an icon column where a row has an icon:
/// every label of a menu starts on one line, a check comes and goes without moving one, and an icon stays.</summary>
[TestFixture]
public class MenuItemGutterThemeTests
{
    private static readonly RoutedCommand Ruler = new("Ruler", typeof(MenuItemGutterThemeTests));

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
            "MacOs" => new Adamantium.UI.Themes.MacOsTheme.MacOs(),
            "Graphite" => new Adamantium.UI.Themes.GraphiteTheme.Graphite(),
            _ => new Adamantium.UI.Themes.FluentTheme.Fluent()
        };
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

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

    private static (ContextMenu Menu, Window Window, Border Owner) Opened(params MenuItem[] rows)
    {
        var menu = new ContextMenu();
        foreach (var row in rows)
        {
            menu.Items.Add(row);
        }

        var owner = new Border { Child = menu };
        var window = new Window { Width = 600, Height = 400, Content = owner };
        Settle(window);
        menu.IsOpen = true;
        Settle(window);
        return (menu, window, owner);
    }

    private static double LabelOf(MenuItem row) =>
        ((IUIComponent)row.GetTemplateChild("PART_ContentPresenter")).Bounds.X;

    private static Visibility Of(MenuItem row, string part) => ((UIComponent)row.GetTemplateChild(part)).Visibility;

    private static MenuItem WithIcon(string header) =>
        new() { Header = header, Icon = new Border { Width = 16, Height = 16 } };

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void EachColumn_IsThereOnlyInAMenuWithARowThatNeedsIt_AndThenOnEveryRow(string theme)
    {
        Use(theme);
        var single = new MenuItem { Header = "1.0" };
        var wide = new MenuItem { Header = "1.5" };
        Opened(single, wide);
        var cut = new MenuItem { Header = "Cut" };
        var copy = WithIcon("Copy");
        Opened(cut, copy);
        var smallCaps = new MenuItem { Header = "Small caps" };
        var ruler = new MenuItem { Header = "Ruler", IsCheckable = true };
        Opened(smallCaps, ruler);

        Assert.Multiple(() =>
        {
            Assert.That(Of(single, "CheckColumn"), Is.EqualTo(Visibility.Collapsed), "no check column");
            Assert.That(Of(single, "IconColumn"), Is.EqualTo(Visibility.Collapsed), "no empty icon column");
            Assert.That(LabelOf(wide), Is.EqualTo(LabelOf(single)).Within(0.01));
            Assert.That(new[] { cut.ShowsIconColumn, copy.ShowsIconColumn }, Is.All.True);
            Assert.That(LabelOf(cut), Is.EqualTo(LabelOf(copy)).Within(0.01), "one line in the menu");
            Assert.That(LabelOf(cut), Is.GreaterThan(LabelOf(single)), "after the icon column");
            Assert.That(Of(cut, "CheckColumn"), Is.EqualTo(Visibility.Collapsed), "no check column");
            Assert.That(new[] { smallCaps.ShowsCheckColumn, ruler.ShowsCheckColumn }, Is.All.True);
            Assert.That(Of(smallCaps, "IconColumn"), Is.EqualTo(Visibility.Collapsed));
            Assert.That(LabelOf(smallCaps), Is.EqualTo(LabelOf(ruler)).Within(0.01), "one line in the menu");
            Assert.That(LabelOf(smallCaps), Is.GreaterThan(LabelOf(single)), "after the check column");
            Assert.That(Of(ruler, "CheckGlyph"), Is.Not.EqualTo(Visibility.Visible), "not checked");
        });
    }

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ACheck_ComesAndGoes_WithoutMovingALabel_AndAnIconStays(string theme)
    {
        Use(theme);
        var bottom = WithIcon("Bottom Border");
        bottom.IsCheckable = true;
        bottom.IsChecked = true;
        var plain = new MenuItem { Header = "Borders and Shading..." };
        var ruler = new MenuItem { Header = "Ruler", IsCheckable = true, IsChecked = true };
        var (_, window, _) = Opened(bottom, plain, ruler);
        var checkedAt = LabelOf(bottom);
        var shown = (Of(bottom, "CheckGlyph"), Of(bottom, "IconPresenter"));

        bottom.IsChecked = false;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.EqualTo((Visibility.Visible, Visibility.Visible)), "the check, and the icon beside it");
            Assert.That(Of(ruler, "CheckGlyph"), Is.EqualTo(Visibility.Visible), "a row with no icon");
            Assert.That(LabelOf(bottom), Is.EqualTo(checkedAt).Within(0.01), "not moved");
            Assert.That(LabelOf(plain), Is.EqualTo(checkedAt).Within(0.01));
            Assert.That(Of(bottom, "CheckGlyph"), Is.Not.EqualTo(Visibility.Visible));
            Assert.That(Of(bottom, "CheckColumn"), Is.EqualTo(Visibility.Visible), "the column stays");
        });
    }

    [Test]
    public void ARowWhoseCommandSaysWhetherItIsOn_CanBeChecked()
    {
        Use("Fluent");
        var on = true;
        var row = new MenuItem { Header = "Ruler", Command = Ruler };
        var plain = new MenuItem { Header = "Zoom" };
        var menu = new ContextMenu { Items = { row, plain } };
        var owner = new Border { Child = menu };
        CommandManager.GetCommandBindings(owner).Add(new CommandBinding(Ruler, (_, _) => on = !on, (_, e) =>
        {
            e.CanExecute = true;
            e.IsChecked = on;
        }));
        var window = new Window { Width = 600, Height = 400, Content = owner };
        Settle(window);
        menu.IsOpen = true;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(row.IsChecked, Is.True);
            Assert.That(plain.ShowsCheckColumn, Is.True);
            Assert.That(Of(row, "CheckGlyph"), Is.EqualTo(Visibility.Visible));
        });
    }

    [Test]
    public void RowsMadeFromData_AndASubmenusRows_KeepTheColumnOfTheirOwnMenu()
    {
        Use("Fluent");
        var data = new ContextMenu { ItemsSource = new List<object> { "Cut", "Copy" } };
        var owner = new Border { Child = data };
        var window = new Window { Width = 600, Height = 400, Content = owner };
        Settle(window);
        data.IsOpen = true;
        Settle(window);
        var cut = (MenuItem)data.ItemContainerGenerator.ContainerFromIndex(0);
        var withoutOne = cut.ShowsCheckColumn;
        cut.IsCheckable = true;
        Settle(window);
        var copy = (MenuItem)data.ItemContainerGenerator.ContainerFromIndex(1);

        var ruler = new MenuItem { Header = "Ruler", IsCheckable = true };
        var view = new MenuItem { Header = "View", Items = { ruler, new MenuItem { Header = "Zoom" } } };
        var (_, outer, _) = Opened(view, new MenuItem { Header = "Print" });
        view.IsSubmenuOpen = true;
        Settle(outer);
        var zoom = (MenuItem)view.Items[1];

        Assert.Multiple(() =>
        {
            Assert.That(withoutOne, Is.False);
            Assert.That(copy.ShowsCheckColumn, Is.True, "a row made from data turned it on");
            Assert.That(zoom.ShowsCheckColumn, Is.True, "the submenu's own rows");
            Assert.That(view.ShowsCheckColumn, Is.False, "the menu the submenu opens from has none");
        });
    }

    [Test]
    public void ARowMovedToAnotherMenu_CountsThereOnly_AndARowInNoMenuShowsItsOwnCheck()
    {
        Use("Fluent");
        var ruler = new MenuItem { Header = "Ruler", IsCheckable = true };
        var cut = new MenuItem { Header = "Cut" };
        var (first, window, _) = Opened(ruler, cut);
        var zoom = new MenuItem { Header = "Zoom" };
        var (second, other, _) = Opened(zoom);

        second.Items.Add(ruler);
        first.Items.Remove(ruler);
        Settle(window);
        Settle(other);
        var alone = new MenuItem { Header = "Alone", IsChecked = true };

        Assert.Multiple(() =>
        {
            Assert.That(cut.ShowsCheckColumn, Is.False, "gone from the first");
            Assert.That(new[] { zoom.ShowsCheckColumn, ruler.ShowsCheckColumn }, Is.All.True, "counted in the second");
            Assert.That(alone.ShowsCheckColumn, Is.True);
        });
    }

    [Test]
    public void AnIconGivenLater_BringsTheIconColumnToEveryRow_AndTakesItAwayWhenTakenOff()
    {
        Use("Fluent");
        var cut = new MenuItem { Header = "Cut" };
        var paste = new MenuItem { Header = "Paste" };
        var (_, window, _) = Opened(cut, paste);

        cut.Icon = new Border { Width = 16, Height = 16 };
        Settle(window);
        var given = (paste.ShowsIconColumn, Of(paste, "IconColumn"));
        cut.Icon = null;
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(given, Is.EqualTo((true, Visibility.Visible)));
            Assert.That(paste.ShowsIconColumn, Is.False);
        });
    }

    [Test]
    public void TheOnlyRowThatCanBeChecked_TakesTheColumnAwayWhenItGoes()
    {
        Use("Fluent");
        var ruler = new MenuItem { Header = "Ruler", IsCheckable = true };
        var zoom = new MenuItem { Header = "Zoom" };
        var (menu, window, _) = Opened(ruler, zoom);
        var with = zoom.ShowsCheckColumn;

        menu.Items.Remove(ruler);
        Settle(window);

        Assert.Multiple(() =>
        {
            Assert.That(with, Is.True);
            Assert.That(zoom.ShowsCheckColumn, Is.False);
        });
    }
}
