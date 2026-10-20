using System.Collections.Generic;
using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>WHERE TEXT DOES NOT SELECT is the theme's to say: in the captions of things a press already means something
/// on - a button, a row of a list, a tab's or an expander's header - and nowhere else. A tab's page and an expander's
/// content are text like any other.</summary>
[TestFixture]
public class TextSelectionThemeTests
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

    private static void Shown(IUIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, Content = content };
        window.ApplyCurrentTheme();
        for (var i = 0; i < 4; i++)
        {
            Adamantium.UI.Extensions.WindowExtension.UpdateTree(window);
            Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        }
    }

    private static IEnumerable<TextBlock> Texts(IUIComponent root)
    {
        foreach (var child in root.VisualChildren)
        {
            if (child is TextBlock text)
            {
                yield return text;
            }

            foreach (var deeper in Texts(child))
            {
                yield return deeper;
            }
        }
    }

    private static TextBlock Saying(IUIComponent root, string text) =>
        Texts(root).FirstOrDefault(block => block.Text == text);

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void PlainTextSelects(string theme)
    {
        Use(theme);
        var block = new TextBlock { Text = "A paragraph" };

        Shown(block);

        Assert.That(block.IsTextSelectionEnabled, Is.True);
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AButtonsCaptionDoesNotSelect(string theme)
    {
        Use(theme);
        var made = new Button { Content = "Save" };
        var given = new Button { Content = new TextBlock { Text = "Open" } };
        var panel = new Adamantium.UI.Controls.Panels.StackPanel();
        panel.Children.Add(made);
        panel.Children.Add(given);

        Shown(panel);

        Assert.Multiple(() =>
        {
            Assert.That(Saying(made, "Save")?.IsTextSelectionEnabled, Is.False, "a caption the button made from text");
            Assert.That(Saying(given, "Open")?.IsTextSelectionEnabled, Is.False, "a text block given as the content");
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ACalendarsTextDoesNotSelect(string theme)
    {
        Use(theme);
        var calendar = new Calendar { ShowWeekNumbers = true };

        Shown(calendar);

        var texts = Texts(calendar).ToList();
        Assert.That(texts, Is.Not.Empty);
        Assert.That(texts.Where(text => text.IsTextSelectionEnabled), Is.Empty, "days, day names, week numbers, header");
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ASegmentsCaptionDoesNotSelect(string theme)
    {
        Use(theme);
        var segments = new SegmentedControl();
        segments.Items.Add(new SegmentedControlItem { Content = "Day" });
        segments.Items.Add(new SegmentedControlItem { Content = "Week" });

        Shown(segments);

        var texts = Texts(segments).ToList();
        Assert.That(texts, Is.Not.Empty);
        Assert.That(texts.Where(text => text.IsTextSelectionEnabled), Is.Empty);
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ATabsHeaderDoesNotSelect_ItsPageDoes(string theme)
    {
        Use(theme);
        var page = new TextBlock { Text = "Page text" };
        var tab = new TabItem { Header = "Tab", Content = page };
        var tabs = new TabControl();
        tabs.Items.Add(tab);

        Shown(tabs);

        Assert.Multiple(() =>
        {
            Assert.That(Saying(tab, "Tab")?.IsTextSelectionEnabled, Is.False, "the header");
            Assert.That(page.IsTextSelectionEnabled, Is.True, "the page");
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AnExpandersHeaderDoesNotSelect_ItsContentDoes(string theme)
    {
        Use(theme);
        var content = new TextBlock { Text = "Inside" };
        var expander = new Expander { Header = "Head", Content = content, IsExpanded = true };

        Shown(expander);

        Assert.Multiple(() =>
        {
            Assert.That(Saying(expander, "Head")?.IsTextSelectionEnabled, Is.False, "the header");
            Assert.That(content.IsTextSelectionEnabled, Is.True, "the content");
        });
    }

    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void AListsRowsDoNotSelect(string theme)
    {
        Use(theme);
        var list = new ListBox();
        list.Items.Add("First");
        list.Items.Add("Second");

        Shown(list);

        Assert.That(Saying(list, "First")?.IsTextSelectionEnabled, Is.False);
    }

    private sealed class Record
    {
        public string Name { get; init; }
    }

    // A row is pressed to pick it, but the panel a row opens is a page of its own.
    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void ADataGridsRowsDoNotSelect_ItsDetailsPanelDoes(string theme)
    {
        Use(theme);
        // A height of its own: a row sized by its content does not take an open panel yet, and that is not what this
        // test is about.
        var grid = new Adamantium.UI.Controls.DataGrid.TreeDataGrid
        {
            RowHeight = 24,
            RowDetailsTemplate = new Adamantium.UI.Core.Templates.DataTemplate(() =>
                new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = new TextBlock { Text = "Details" } })
        };
        grid.Columns.Add(new Adamantium.UI.Controls.DataGrid.DataGridTextColumn
        {
            Binding = new Adamantium.UI.Core.Data.Binding("Name")
        });
        var records = new List<Record> { new() { Name = "Cell" }, new() { Name = "Other" } };
        grid.ItemsSource = records;
        grid.ToggleRowDetails(records[0]);

        Shown(grid);

        Assert.Multiple(() =>
        {
            Assert.That(Saying(grid, "Cell")?.IsTextSelectionEnabled, Is.False, "a cell of a row");
            Assert.That(Saying(grid, "Details")?.IsTextSelectionEnabled, Is.True, "the details panel");
        });
    }

    // A pane is dragged by its header, so the title in it does not select.
    [Test]
    [TestCase("Fluent")]
    [TestCase("Graphite")]
    [TestCase("MacOs")]
    public void APanesTitleDoesNotSelect_ItsContentDoes(string theme)
    {
        Use(theme);
        var content = new TextBlock { Text = "Pane text" };
        var group = new PaneGroup { Zone = DockZone.Right };
        group.Items.Add(new Pane { Header = "Tools", Id = "Tools", Kind = PaneKind.Tool, Content = content });
        var area = new DockingArea { Children = { group } };

        Shown(area);

        var titles = Texts(group).Where(block => block.Text == "Tools").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(titles, Is.Not.Empty);
            Assert.That(titles.Select(block => block.IsTextSelectionEnabled), Is.All.False, "the title and the tab");
            Assert.That(content.IsTextSelectionEnabled, Is.True, "the content");
        });
    }
}
