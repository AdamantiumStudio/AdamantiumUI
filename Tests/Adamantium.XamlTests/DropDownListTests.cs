using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Extensions;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A drop-down's list under each theme: a long one - every font of the system - makes the rows it shows, not
/// one for each item, so it opens as fast as a short one.</summary>
[TestFixture]
public class DropDownListTests
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

    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ALongList_MakesTheRowsItShows_NotOneForEachItem(string theme)
    {
        Use(theme);
        var box = Shown(new DropDown { ItemsSource = Families(300) }, out _);

        var made = box.ItemContainerGenerator.RealizedIndices.Count();

        Assert.Multiple(() =>
        {
            Assert.That(made, Is.GreaterThan(0), "the list is open");
            Assert.That(made, Is.LessThan(40), "a list as tall as MaxDropDownHeight shows about a dozen rows");
        });
    }

    // Opened on an item far down, the list shows it: its row is made where it stands.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ALongList_OpensOnTheSelectedItem_FarDown(string theme)
    {
        Use(theme);
        var box = Shown(new DropDown { ItemsSource = Families(300), SelectedIndex = 250 }, out _);

        Assert.That(box.ItemContainerGenerator.RealizedIndices, Does.Contain(250));
    }

    // The arrows walk past the rows shown: the list follows the highlight, made row by row.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void TheArrows_WalkPastTheRowsShown_AndTheListFollows(string theme)
    {
        Use(theme);
        var box = Shown(new DropDown { ItemsSource = Families(300), SelectedIndex = 0 }, out var window);

        Down(box, window, 40);
        Pass(window, 3);
        var row = box.ItemContainerGenerator.ContainerFromIndex(40) as DropDownItem;

        Assert.Multiple(() =>
        {
            Assert.That(row, Is.Not.Null, "the row walked to is made");
            Assert.That(row?.IsHighlighted, Is.True);
        });
    }

    private static System.Collections.Generic.List<string> Families(int count) =>
        Enumerable.Range(0, count).Select(i => "Family " + i).ToList();

    private static DropDown Shown(DropDown box, out Window window)
    {
        box.Width = 160;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.VerticalAlignment = VerticalAlignment.Top;
        window = new Window { Width = 800, Height = 600, Content = box };
        window.ApplyCurrentTheme();
        WindowExtension.UpdateTree(window);
        box.IsDropDownOpen = true;
        Pass(window, 6);
        return box;
    }

    private static void Down(DropDown box, Window window, int times)
    {
        for (var i = 0; i < times; i++)
        {
            box.RaiseEvent(new Adamantium.UI.Core.Input.KeyEventArgs(Adamantium.UI.Core.Input.KeyboardDevice.CurrentDevice,
                Adamantium.UI.Core.Input.Key.DownArrow, Adamantium.UI.Core.Input.InputModifiers.None, 0)
            {
                RoutedEvent = Adamantium.UI.Core.Input.Keyboard.KeyDownEvent
            });
            Pass(window, 1);
        }
    }

    // Rows written in markup are their own items: walked to past the rows shown, the one reached is highlighted, the one
    // left is not.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void RowsWrittenInMarkup_KeepTheHighlight_PastTheRowsShown(string theme)
    {
        Use(theme);
        var box = new DropDown();
        for (var i = 0; i < 60; i++) box.Items.Add(new DropDownItem { Content = "Row " + i });
        box.SelectedIndex = 0;
        Shown(box, out var window);

        Down(box, window, 30);
        Pass(window, 3);

        Assert.Multiple(() =>
        {
            Assert.That(((DropDownItem)box.Items[30]).IsHighlighted, Is.True);
            Assert.That(((DropDownItem)box.Items[0]).IsHighlighted, Is.False);
            Assert.That(box.Items.Cast<DropDownItem>().Count(row => row.IsHighlighted), Is.EqualTo(1));
        });
    }

    // The pointer moving onto a row half shown highlights it and leaves the list where it is: a list that jumped under a
    // still pointer would fight the wheel.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ThePointerOnARow_HighlightsIt_AndLeavesTheListWhereItIs(string theme)
    {
        Use(theme);
        var box = Shown(new DropDown { ItemsSource = Families(300) }, out var window);
        var panel = (Adamantium.UI.Controls.Panels.VirtualizingPanel)box.ItemsHostPanel;
        var last = box.ItemContainerGenerator.RealizedIndices.Max();
        var before = panel.Offset;

        box.HighlightFromContainer((DropDownItem)box.ItemContainerGenerator.ContainerFromIndex(last));
        Pass(window, 3);

        Assert.Multiple(() =>
        {
            Assert.That(((DropDownItem)box.ItemContainerGenerator.ContainerFromIndex(last)).IsHighlighted, Is.True);
            Assert.That(panel.Offset, Is.EqualTo(before));
        });
    }

    // Of two equal items the one highlighted is the one shown and lit, not the first like it.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void OfTwoEqualItems_TheHighlightedOne_IsShownAndLit(string theme)
    {
        Use(theme);
        var items = Enumerable.Range(0, 202).Select(i => i is 0 or 201 ? "Arial" : "Family " + i).ToList();
        var box = Shown(new DropDown { ItemsSource = items, SelectedIndex = 201 }, out var window);

        var lit = box.ItemContainerGenerator.RealizedIndices
            .Where(i => ((DropDownItem)box.ItemContainerGenerator.ContainerFromIndex(i)).IsHighlighted).ToList();

        Assert.That(lit, Is.EqualTo(new[] { 201 }));
    }

    // A list on a panel that makes every row - mixed heights - opens on the value far down too.
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AListThatMakesEveryRow_OpensOnTheValueFarDown(string theme)
    {
        Use(theme);
        var box = new DropDown
        {
            ItemsSource = Families(100),
            SelectedIndex = 80,
            ItemsPanel = new ItemsPanelTemplate(() => new Adamantium.UI.Core.Templates.TemplateResult
            {
                RootComponent = new Adamantium.UI.Controls.Panels.StackPanel { IsVirtualizing = false }
            }),
        };
        Shown(box, out var window);

        var row = (IUIComponent)box.ItemContainerGenerator.ContainerFromIndex(80);
        var viewer = ((IUIComponent)box.ItemsHostPanel).GetVisualAncestors().OfType<Adamantium.UI.Controls.ScrollViewer>().First();
        var top = row.TranslatePoint(new Adamantium.Mathematics.Vector2<double>(0, 0), viewer).Y;

        Assert.That(top, Is.InRange(0, viewer.RenderSize.Height - row.RenderSize.Height + 1));
    }

    // A frame of the loop: what was posted for it, the window's layout, the popup layer's.
    private static void Pass(Window window, int times)
    {
        for (var i = 0; i < times; i++)
        {
            Adamantium.UI.Core.LoopSignal.Drain();
            WindowExtension.UpdateTree(window);
            window.PopupLayer.UpdateLayout(new Adamantium.Mathematics.Size<double>(window.Width, window.Height));
        }
    }
}
