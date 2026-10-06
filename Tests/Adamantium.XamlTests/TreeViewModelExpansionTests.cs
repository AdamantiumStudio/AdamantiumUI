using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Diagnostics;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A tree whose item container style binds the rows' expansion and selection to the nodes: the view model opens a node by
/// setting its <c>IsExpanded</c>. The tree wrote the rows' own state at Local priority - binding a row, selecting it,
/// toggling it - which outranks the style's binding, so after a click the view model could no longer open the row.
/// </summary>
[TestFixture]
public class TreeViewModelExpansionTests
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

    [Test]
    public async Task AClickedRow_IsStillOpenedByTheViewModel()
    {
        var folder = new Node("Folder", new Node("File"));
        var containerStyle = new Style { Selector = new StyleSelector { Types = { typeof(TreeViewItem) } } };
        containerStyle.Setters.Add(new Setter(nameof(TreeViewItem.IsExpanded), new Binding(nameof(Node.IsExpanded)) { Mode = BindingMode.TwoWay }));
        containerStyle.Setters.Add(new Setter(nameof(TreeViewItem.IsSelected), new Binding(nameof(Node.IsSelected)) { Mode = BindingMode.TwoWay }));
        var tree = new TreeView
        {
            Name = "Tree",
            ItemContainerStyle = containerStyle,
            ItemTemplate = new HierarchicalDataTemplate(() =>
            {
                var title = new TextBlock();
                title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Node.Title)));
                return new TemplateResult { RootComponent = title };
            })
            {
                ItemsSource = new Binding(nameof(Node.Children))
            },
            ItemsSource = new List<Node> { folder }
        };
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = tree };
        await using var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        var row = session.Find(By.Id("Tree")).Find(By.Name("Folder"));

        await row.ClickAsync();
        folder.IsExpanded = true;
        await session.WaitForIdleAsync();
        var opened = await row.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(folder.IsSelected, Is.True, "the click reached the node");
            Assert.That(opened.ExpandCollapseState, Is.EqualTo("Expanded"), "and the view model still opens the row");
        });
    }

    [Test]
    public async Task ALeafOfAnotherKind_WithoutTheMember_IsNoBrokenBinding_AndTheViewModelStillOpensItsBranch()
    {
        var folder = new Node("Folder");
        folder.Kinds.Add(new Leaf("File"));
        var containerStyle = new Style { Selector = new StyleSelector { Types = { typeof(TreeViewItem) } } };
        containerStyle.Setters.Add(new Setter(nameof(TreeViewItem.IsExpanded), new Binding(nameof(Node.IsExpanded)) { Mode = BindingMode.TwoWay }));
        var tree = new TreeView
        {
            Name = "Tree",
            ItemContainerStyle = containerStyle,
            ItemTemplate = new HierarchicalDataTemplate(() =>
            {
                var title = new TextBlock();
                title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Node.Title)));
                return new TemplateResult { RootComponent = title };
            })
            {
                ItemsSource = new Binding(nameof(Node.Kinds))
            },
            ItemsSource = new List<Node> { folder }
        };
        var messages = new List<string>();
        BindingTrace.Sink = messages.Add;
        try
        {
            var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = tree };
            await using var session = AutomationSession.InProcess(window);
            await session.WaitForIdleAsync();

            folder.IsExpanded = true;
            await session.WaitForIdleAsync();
            var leaf = await session.Find(By.Id("Tree")).Find(By.Name("File")).ExistsAsync();
            await session.Find(By.Id("Tree")).Find(By.Name("Folder")).CollapseAsync();

            Assert.Multiple(() =>
            {
                Assert.That(leaf, Is.True, "the view model opened the branch");
                Assert.That(folder.IsExpanded, Is.False, "and the tree wrote the node back when the row closed");
                Assert.That(messages, Has.None.Contains("IsExpanded"), "a leaf without the member is not a broken binding");
            });
        }
        finally
        {
            BindingTrace.Sink = null;
        }
    }

    private sealed class Leaf(string title)
    {
        public string Title { get; } = title;
    }

    private sealed class Node : INotifyPropertyChanged
    {
        private bool _isExpanded;
        private bool _isSelected;

        public Node(string title, params Node[] children)
        {
            Title = title;
            Children = [.. children];
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title { get; }

        public List<Node> Children { get; }

        public List<object> Kinds { get; } = [];

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
    }
}
