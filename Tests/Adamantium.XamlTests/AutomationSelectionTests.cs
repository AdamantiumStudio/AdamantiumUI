using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Controls.DrawingBoard;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Selections of many through automation: an item is added to what is selected and taken out of it, leaving the rest,
/// and selecting one makes it the only one - in a list, a data grid's rows and cells and on a canvas. A container that
/// selects one at a time refuses a second.
/// </summary>
[TestFixture]
public class AutomationSelectionTests
{
    private FakeApp _app;

    private sealed class Part
    {
        public string Code { get; set; }
    }

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [SetUp]
    public void Fresh()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static async Task<AutomationSession> Driving(UIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task AListOfMany_AddsAndTakesOut_AndSelectingOneLeavesItAlone()
    {
        var list = new ListBox { Name = "Fruits", SelectionMode = SelectionMode.Multiple, ItemsSource = new List<string> { "Apple", "Pear", "Plum" } };
        var session = await Driving(list);
        await using var _ = session;
        var apple = session.Find(By.Id("Fruits")).Find(By.Name("Apple"));
        var pear = session.Find(By.Id("Fruits")).Find(By.Name("Pear"));

        await apple.SelectAsync();
        await pear.AddToSelectionAsync();
        var both = list.SelectedItems.Cast<object>().ToList();
        await apple.RemoveFromSelectionAsync();
        var pearOnly = list.SelectedItems.Cast<object>().ToList();
        await pear.SelectAsync();
        await pear.SelectAsync();

        Assert.Multiple(() =>
        {
            Assert.That(both, Is.EquivalentTo(new[] { "Apple", "Pear" }));
            Assert.That(pearOnly, Is.EqualTo(new[] { "Pear" }));
            Assert.That(list.SelectedItems.Cast<object>(), Is.EqualTo(new[] { "Pear" }),
                "selecting the selected one again keeps it selected - a click would have toggled it off");
        });
    }

    [Test]
    public async Task ATreeOfMany_AddsAndTakesOut_AndSelectingOneLeavesItAlone()
    {
        var tree = new TreeView
        {
            Name = "Kinds",
            SelectionMode = TreeViewSelectionMode.Multiple,
            ItemTemplate = new Adamantium.UI.Core.Templates.HierarchicalDataTemplate(() =>
            {
                var title = new Adamantium.UI.Controls.Text.TextBlock();
                title.SetBinding(Adamantium.UI.Controls.Text.TextBlock.TextProperty, new Binding(nameof(Part.Code)));
                return new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = title };
            }),
            ItemsSource = new List<Part> { new() { Code = "Cat" }, new() { Code = "Dog" }, new() { Code = "Oak" } }
        };
        var session = await Driving(tree);
        await using var _ = session;
        var cat = session.Find(By.Id("Kinds")).Find(By.Type(AutomationControlType.TreeItem).And(By.Name("Cat")));
        var dog = session.Find(By.Id("Kinds")).Find(By.Type(AutomationControlType.TreeItem).And(By.Name("Dog")));

        await cat.SelectAsync();
        await dog.AddToSelectionAsync();
        var both = ((await cat.GetAsync()).IsSelected, (await dog.GetAsync()).IsSelected);
        await cat.RemoveFromSelectionAsync();
        var dogOnly = ((await cat.GetAsync()).IsSelected, (await dog.GetAsync()).IsSelected);
        await dog.SelectAsync();
        await dog.SelectAsync();
        var again = (await dog.GetAsync()).IsSelected;

        Assert.Multiple(() =>
        {
            Assert.That(both, Is.EqualTo((true, true)));
            Assert.That(dogOnly, Is.EqualTo((false, true)));
            Assert.That(again, Is.True, "selected again, it stays selected - a click would have toggled it off");
        });
    }

    [Test]
    public async Task AListOfOne_RefusesASecond()
    {
        var list = new ListBox { Name = "Fruits", ItemsSource = new List<string> { "Apple", "Pear" } };
        var session = await Driving(list);
        await using var _ = session;

        await session.Find(By.Id("Fruits")).Find(By.Name("Apple")).SelectAsync();
        var refused = Assert.ThrowsAsync<AutomationException>(() =>
            session.Find(By.Id("Fruits")).Find(By.Name("Pear")).AddToSelectionAsync());

        Assert.That(refused.Message, Does.Contain("One item is selected here at a time"));
    }

    [Test]
    public async Task AGridsRowsAndCells_AreAddedAndTakenOut()
    {
        var grid = new TreeDataGrid
        {
            Name = "Parts",
            Height = 300,
            ItemsSource = new List<Part> { new() { Code = "A" }, new() { Code = "B" }, new() { Code = "C" } }
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Code", Binding = new Binding(nameof(Part.Code)) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Again", Binding = new Binding(nameof(Part.Code)) });
        var session = await Driving(grid);
        await using var _ = session;
        var rows = session.Find(By.Id("Parts")).Child(By.Type(AutomationControlType.DataItem));

        bool Whole(int row) => grid.SelectedCells.Contains(row, 0) && grid.SelectedCells.Contains(row, 1);
        await rows.At(0).SelectAsync();
        await rows.At(2).AddToSelectionAsync();
        var twoRows = (Whole(0), Whole(1), Whole(2));
        await rows.At(0).RemoveFromSelectionAsync();
        var lastRow = (Whole(0), Whole(2));
        await rows.At(2).Child(By.Name("C")).At(1).RemoveFromSelectionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(twoRows, Is.EqualTo((true, false, true)));
            Assert.That(lastRow, Is.EqualTo((false, true)));
            Assert.That(grid.SelectedCells.Contains(2, 0), Is.True);
            Assert.That(grid.SelectedCells.Contains(2, 1), Is.False, "one cell out of a selected row");
        });
    }

    [Test]
    public async Task CanvasNodes_AreAddedAndTakenOut()
    {
        var blur = new CanvasNode { Title = "Blur" };
        var sharpen = new CanvasNode { Title = "Sharpen" };
        var canvas = new InfiniteCanvas { Mode = CanvasMode.Nodes, Scene = new CanvasScene() };
        var blurItem = new ElementItem(blur, new Rect(-350, -150, 190, 110));
        var sharpenItem = new ElementItem(sharpen, new Rect(-50, -150, 190, 110));
        canvas.Scene.Add(blurItem);
        canvas.Scene.Add(sharpenItem);
        var session = await Driving(canvas);
        await using var _ = session;

        await session.Find(By.Name("Blur")).SelectAsync();
        await session.Find(By.Name("Sharpen")).AddToSelectionAsync();
        var both = canvas.Selection.ToList();
        await session.Find(By.Name("Blur")).RemoveFromSelectionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(both, Is.EquivalentTo(new ICanvasItem[] { blurItem, sharpenItem }));
            Assert.That(canvas.Selection, Is.EqualTo(new ICanvasItem[] { sharpenItem }));
        });
    }

    [Test]
    public async Task AScenarioAddsWithAFlag_AndUnselects()
    {
        var list = new ListBox { Name = "Fruits", SelectionMode = SelectionMode.Multiple, ItemsSource = new List<string> { "Apple", "Pear", "Plum" } };
        var session = await Driving(list);
        await using var _ = session;

        await session.RunCommandAsync("select id=Fruits/name=Apple", null);
        await session.RunCommandAsync("select id=Fruits/name=Plum --add", null);
        var both = list.SelectedItems.Cast<object>().ToList();
        await session.RunCommandAsync("unselect id=Fruits/name=Apple", null);

        Assert.Multiple(() =>
        {
            Assert.That(both, Is.EquivalentTo(new[] { "Apple", "Plum" }));
            Assert.That(list.SelectedItems.Cast<object>(), Is.EqualTo(new[] { "Plum" }));
        });
    }
}
