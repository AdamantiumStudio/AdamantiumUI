using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.DataGrid;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A data grid to automation: column headers, then rows - a stand-in where the virtualizing rows made none - called by
/// their first column; a header sorts, a cell takes a value the way committing an edit does, a row is selected and a
/// branch opened, with or without an element.
/// </summary>
[TestFixture]
public class AutomationGridTests
{
    private FakeApp _app;

    private sealed class Part
    {
        public string Code { get; set; }

        public int Size { get; set; }

        public ObservableCollection<Part> Children { get; } = [];
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

    private static (TreeDataGrid Grid, ObservableCollection<Part> Parts) Table(int count = 500)
    {
        var parts = new ObservableCollection<Part>();
        for (var index = 0; index < count; index++)
        {
            var part = new Part { Code = $"P-{index:0000}", Size = index };
            if (index % 5 == 0)
            {
                part.Children.Add(new Part { Code = $"P-{index:0000}.1" });
            }

            parts.Add(part);
        }

        var grid = new TreeDataGrid
        {
            Name = "Parts",
            Height = 300,
            ChildrenPath = nameof(Part.Children),
            ItemsSource = parts
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Code", Binding = new Binding(nameof(Part.Code)) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Size", Binding = new Binding(nameof(Part.Size)) });
        return (grid, parts);
    }

    private static async Task<AutomationSession> Driving(UIComponent content)
    {
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = content };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task TheGrid_IsHeadersThenRows_AStandInForARowWithNoElement()
    {
        var (grid, _) = Table();
        await using var session = await Driving(grid);

        var headers = await session.Find(By.Id("Parts")).Find(By.Type(AutomationControlType.HeaderItem)).GetAsync();
        var first = await session.Find(By.Id("Parts")).Find(By.Name("P-0000")).GetAsync();
        var far = await session.Find(By.Id("Parts")).Find(By.Name("P-0400")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(headers.Name, Is.EqualTo("Code"));
            Assert.That(first.ControlType, Is.EqualTo("DataItem"));
            Assert.That(first.ClassName, Is.EqualTo(nameof(DataGridRow)));
            Assert.That(far.IsOffscreen, Is.True, "a row the virtualizing rows did not make stands as a stand-in");
        });
    }

    [Test]
    public async Task AHeader_SortsByItsColumn_AsAClickDoes()
    {
        var (grid, _) = Table();
        await using var session = await Driving(grid);
        var size = session.Find(By.Id("Parts")).Find(By.Name("Size"));

        await size.InvokeAsync();
        await size.InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(grid.SortColumn, Is.SameAs(grid.Columns[1]));
            Assert.That(grid.SortDescending, Is.True, "a second press turns the sort around");
        });
    }

    [Test]
    public async Task ACell_TakesAValue_AndTheGridRefusesOneItsColumnCannotHold()
    {
        var (grid, parts) = Table();
        await using var session = await Driving(grid);
        var row = session.Find(By.Id("Parts")).Find(By.Name("P-0001"));

        await row.Find(By.Name("1")).SetValueAsync("42");
        var refusal = Assert.ThrowsAsync<AutomationException>(() => row.Find(By.Name("42")).SetValueAsync("forty"));

        Assert.Multiple(() =>
        {
            Assert.That(parts[1].Size, Is.EqualTo(42), "written through the column's binding");
            Assert.That(refusal.Message, Does.Contain("refused"));
        });
    }

    [Test]
    public async Task ARow_IsSelected_AndAFarOneIsBroughtIntoViewFirst()
    {
        var (grid, _) = Table();
        await using var session = await Driving(grid);
        var far = session.Find(By.Id("Parts")).Find(By.Name("P-0300"));

        await far.ScrollIntoViewAsync();
        await far.SelectAsync();
        var selected = await far.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(selected.IsOffscreen, Is.False);
            Assert.That(selected.IsSelected, Is.True);
            Assert.That(grid.SelectedCells.ContainsRow(300), Is.True);
        });
    }

    [Test]
    public async Task ABranch_OpensWithOrWithoutAnElement()
    {
        var (grid, parts) = Table();
        await using var session = await Driving(grid);

        await session.Find(By.Id("Parts")).Find(By.Name("P-0000")).ExpandAsync();
        await session.Find(By.Id("Parts")).Find(By.Name("P-0400")).ExpandAsync();
        var child = await session.Find(By.Id("Parts")).Find(By.Name("P-0400.1")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(grid.IsExpanded(parts[0]), Is.True);
            Assert.That(grid.IsExpanded(parts[400]), Is.True, "a stand-in row opens through the grid as an element would");
            Assert.That(child.Name, Is.EqualTo("P-0400.1"));
        });
    }

    [Test]
    public async Task OpeningDetailsWithTabs_LeavesNoErrorBehind()
    {
        var (grid, parts) = Table(20);
        grid.RowDetailsTemplate = new DataTemplate(() =>
        {
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "One", Content = new Border { Height = 40 } });
            tabs.Items.Add(new TabItem { Header = "Two", Content = new Border { Height = 20 } });
            return new TemplateResult { RootComponent = tabs };
        });
        await using var session = await Driving(grid);
        var mark = await session.MarkAsync();

        grid.ToggleRowDetails(parts[1]);
        await session.WaitForIdleAsync();
        grid.ToggleRowDetails(parts[1]);
        await session.WaitForIdleAsync();
        grid.ToggleRowDetails(parts[1]);
        await session.WaitForIdleAsync();

        Assert.That((await session.ErrorsSinceAsync(mark)).Select(error => error.Message), Is.Empty);
    }
}
