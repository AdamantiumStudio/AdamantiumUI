using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Lists, drop-downs and menus to automation: their items are their children in order - the item's element where it has
/// one, a stand-in where a virtualizing list has not made it or a closed list has not built it - and a drop-down's list
/// and a submenu belong to the control that opens them.
/// </summary>
[TestFixture]
public class AutomationListTests
{
    private FakeApp _app;

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

    private static async Task<AutomationSession> Driving(UIComponent content, double height = 600)
    {
        var window = new Window { Width = 800, Height = height, ClientWidth = 800, ClientHeight = height, Content = content };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task AListsItems_AreItsChildren_AndOneIsSelected()
    {
        var list = new ListBox { Name = "Fruits", ItemsSource = new List<string> { "Apple", "Pear", "Plum" } };
        await using var session = await Driving(list);

        var items = await session.FindAllAsync(By.Id("Fruits"));
        await session.Find(By.Id("Fruits")).Find(By.Name("Pear")).SelectAsync();
        var pear = await session.Find(By.Id("Fruits")).Find(By.Name("Pear")).GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(items.Single().ControlType, Is.EqualTo("List"));
            Assert.That(list.SelectedItem, Is.EqualTo("Pear"));
            Assert.That(pear.ControlType, Is.EqualTo("ListItem"));
            Assert.That(pear.IsSelected, Is.True);
        });
    }

    [Test]
    public async Task AnItemTheListHasNotMade_IsFound_Selected_AndBroughtIntoView()
    {
        var rows = Enumerable.Range(0, 2000).Select(i => $"Row {i}").ToList();
        var list = new ListBox { Name = "Rows", ItemsSource = rows };
        await using var session = await Driving(list, 300);
        var far = session.Find(By.Id("Rows")).Find(By.Name("Row 1500"));

        var before = await far.GetAsync();
        await far.SelectAsync();
        await far.ScrollIntoViewAsync();
        var after = await far.GetAsync();
        await far.ClickAsync();

        Assert.Multiple(() =>
        {
            Assert.That(before.IsOffscreen, Is.True, "no element yet: it stands in for the row");
            Assert.That(before.ClassName, Is.EqualTo("String"));
            Assert.That(list.SelectedItem, Is.EqualTo("Row 1500"), "selected without an element");
            Assert.That(after.ClassName, Is.EqualTo("ListBoxItem"), "brought into view, it has its element");
            Assert.That(after.IsOffscreen, Is.False);
        });
    }

    [Test]
    public async Task ADropDownsList_BelongsToIt_AndPicksAnItem()
    {
        var plan = new DropDown { Name = "Plan", ItemsSource = new List<string> { "Free", "Pro", "Team" } };
        await using var session = await Driving(plan);
        var dropDown = session.Find(By.Id("Plan"));

        await dropDown.ExpandAsync();
        var expanded = await dropDown.GetAsync();
        await dropDown.Find(By.Name("Pro")).SelectAsync();
        var picked = await dropDown.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(expanded.ControlType, Is.EqualTo("ComboBox"));
            Assert.That(expanded.ExpandCollapseState, Is.EqualTo("Expanded"));
            Assert.That(plan.SelectedItem, Is.EqualTo("Pro"));
            Assert.That(picked.Value, Is.EqualTo("Pro"));
        });
    }

    [Test]
    public async Task APopupPutAwayByAPressElsewhere_OpensAgain()
    {
        var plan = new DropDown { Name = "Plan", ItemsSource = new List<string> { "Free", "Pro" }, Width = 200 };
        var elsewhere = new Button { Name = "Elsewhere", Content = "Elsewhere", Width = 200 };
        var stack = new StackPanel();
        stack.Children.Add(plan);
        stack.Children.Add(new Border { Height = 300 });
        stack.Children.Add(elsewhere);
        await using var session = await Driving(stack);
        var dropDown = session.Find(By.Id("Plan"));

        await dropDown.ExpandAsync();
        await session.Find(By.Id("Elsewhere")).ClickAsync();
        var putAway = await dropDown.GetAsync();
        await dropDown.ExpandAsync();
        var again = await dropDown.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(putAway.ExpandCollapseState, Is.EqualTo("Collapsed"), "the press elsewhere put it away");
            Assert.That(again.ExpandCollapseState, Is.EqualTo("Expanded"), "and it opens again");
        });
    }

    [Test]
    public async Task ATreesRows_NestAgain_AndABranchOpensAndIsChosenFrom()
    {
        var dog = new Node("Dog");
        var tree = new TreeView
        {
            Name = "Kinds",
            ItemTemplate = new HierarchicalDataTemplate(() =>
            {
                var title = new TextBlock();
                title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Node.Title)));
                return new TemplateResult { RootComponent = title };
            })
            {
                ItemsSource = new Binding(nameof(Node.Children))
            },
            ItemsSource = new List<Node> { new("Animals", new Node("Cat"), dog), new("Plants", new Node("Oak")) }
        };
        await using var session = await Driving(tree);
        var animals = session.Find(By.Id("Kinds")).Find(By.Name("Animals"));

        var collapsed = await animals.GetAsync();
        await animals.ExpandAsync();
        var expanded = await animals.GetAsync();
        await animals.Find(By.Name("Dog")).SelectAsync();
        var path = (await animals.Find(By.Name("Dog")).GetAsync()).Path;

        Assert.Multiple(() =>
        {
            Assert.That(collapsed.ControlType, Is.EqualTo("TreeItem"));
            Assert.That(collapsed.ExpandCollapseState, Is.EqualTo("Collapsed"));
            Assert.That(expanded.ExpandCollapseState, Is.EqualTo("Expanded"));
            Assert.That(tree.SelectedItem, Is.SameAs(dog));
            Assert.That(path, Does.Contain("TreeItem \"Animals\"").And.EndWith("TreeItem \"Dog\" [TreeViewItem]"));
        });
    }

    [Test]
    public async Task ASubmenu_StaysOpen_WhileThePointerMovesIntoIt()
    {
        var parent = new MenuItem { Header = "Open" };
        parent.Items.Add(new MenuItem { Header = "Recent" });
        var menu = new ContextMenu { Name = "Menu" };
        menu.Items.Add(parent);
        menu.Items.Add(new MenuItem { Header = "Save" });
        var target = new Button { Name = "Target", Content = "Target", ContextMenu = menu };
        await using var session = await Driving(target);
        var row = session.Find(By.Id("Menu")).Find(By.Name("Open"));

        await session.Find(By.Id("Target")).RightClickAsync();
        await row.HoverAsync();
        var opened = await row.GetAsync();
        await row.Find(By.Name("Recent")).HoverAsync();
        await Task.Delay(800);
        await session.WaitForIdleAsync();
        var after = await row.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(opened.ExpandCollapseState, Is.EqualTo("Expanded"), "hovering the row opens its submenu");
            Assert.That(after.ExpandCollapseState, Is.EqualTo("Expanded"), "and moving into the submenu keeps it open");
        });
    }

    [Test]
    public async Task ASubmenusRows_BelongToTheirParentRow()
    {
        var chosen = 0;
        var child = new MenuItem { Header = "Recent" };
        child.Click += (_, _) => chosen++;
        var parent = new MenuItem { Header = "Open" };
        parent.Items.Add(child);
        var menu = new ContextMenu { Name = "Menu" };
        menu.Items.Add(parent);
        menu.Items.Add(new MenuItem { Header = "Save" });
        var target = new Button { Name = "Target", Content = "Target", ContextMenu = menu };
        await using var session = await Driving(target);

        await session.Find(By.Id("Target")).RightClickAsync();
        var row = session.Find(By.Id("Menu")).Find(By.Name("Open"));
        await row.ExpandAsync();
        var open = await row.GetAsync();
        await row.Find(By.Name("Recent")).InvokeAsync();

        Assert.Multiple(() =>
        {
            Assert.That(open.ControlType, Is.EqualTo("MenuItem"));
            Assert.That(open.ExpandCollapseState, Is.EqualTo("Expanded"));
            Assert.That(chosen, Is.EqualTo(1));
        });
    }

    private sealed class Node
    {
        public Node(string title, params Node[] children)
        {
            Title = title;
            Children = [.. children];
        }

        public string Title { get; }

        public List<Node> Children { get; }
    }
}
