using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Data;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A node that gives its children anew - a list read again from disk, as a folder's - is read again by the tree its
/// template's <c>ItemsSource</c> binding names: a leaf that gained children shows its expander and opens, an open branch
/// shows the new children. The tree read the member once, as the row was built, so a folder that was empty stayed a leaf.
/// </summary>
[TestFixture]
public class TreeViewChildrenReplacedTests
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
    public async Task ALeafThatGainsChildren_ShowsItsExpander_AndOpens()
    {
        var content = new Folder("Content");
        await using var session = await ShowAsync(content);
        var row = session.Find(By.Id("Tree")).Find(By.Name("Content"));
        var before = (await row.GetAsync()).ExpandCollapseState;

        content.Give(new Folder("Models"));
        await session.WaitForIdleAsync();
        var after = (await row.GetAsync()).ExpandCollapseState;
        await row.ExpandAsync();
        var models = await session.Find(By.Id("Tree")).Find(By.Name("Models")).ExistsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo("LeafNode"));
            Assert.That(after, Is.EqualTo("Collapsed"), "the row has an expander now");
            Assert.That(models, Is.True, "and opens on the new child");
        });
    }

    [Test]
    public async Task AnOpenBranchGivenOtherChildren_ShowsThem()
    {
        var content = new Folder("Content", new Folder("Old"));
        await using var session = await ShowAsync(content);
        var tree = session.Find(By.Id("Tree"));
        await tree.Find(By.Name("Content")).ExpandAsync();

        content.Give(new Folder("New A"), new Folder("New B"));
        await session.WaitForIdleAsync();
        var old = await tree.Find(By.Name("Old")).ExistsAsync();
        var newA = await tree.Find(By.Name("New A")).ExistsAsync();
        var newB = await tree.Find(By.Name("New B")).ExistsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(old, Is.False);
            Assert.That(newA, Is.True);
            Assert.That(newB, Is.True);
        });
    }

    [Test]
    public async Task ALeafTheViewModelOpened_OpensOnItsChildrenWhenItGetsThem()
    {
        var content = new Folder("Content");
        await using var session = await ShowAsync(content, bindsExpansion: true);
        content.IsExpanded = true;
        await session.WaitForIdleAsync();

        content.Give(new Folder("Models"));
        await session.WaitForIdleAsync();
        var models = await session.Find(By.Id("Tree")).Find(By.Name("Models")).ExistsAsync();

        Assert.That(models, Is.True, "the row was asked open before it had anything to show");
    }

    private static async Task<AutomationSession> ShowAsync(Folder root, bool bindsExpansion = false)
    {
        Style containerStyle = null;
        if (bindsExpansion)
        {
            containerStyle = new Style { Selector = new StyleSelector { Types = { typeof(TreeViewItem) } } };
            containerStyle.Setters.Add(new Setter(nameof(TreeViewItem.IsExpanded),
                new Binding(nameof(Folder.IsExpanded)) { Mode = BindingMode.TwoWay }));
        }

        var tree = new TreeView
        {
            Name = "Tree",
            ItemContainerStyle = containerStyle,
            ItemTemplate = new HierarchicalDataTemplate(() =>
            {
                var title = new TextBlock();
                title.SetBinding(TextBlock.TextProperty, new Binding(nameof(Folder.Name)));
                return new TemplateResult { RootComponent = title };
            })
            {
                ItemsSource = new Binding(nameof(Folder.Children))
            },
            ItemsSource = new List<Folder> { root }
        };
        var window = new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = tree };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    // As the editor's content folder: the folders in it are a plain list, given anew when read again.
    private sealed class Folder(string name, params Folder[] children) : INotifyPropertyChanged
    {
        private bool _isExpanded;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Name { get; } = name;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }

        public IReadOnlyList<Folder> Children { get; private set; } = children;

        public void Give(params Folder[] children)
        {
            Children = children;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Children)));
        }
    }
}
