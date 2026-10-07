using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.EditorProTheme;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What an open popup shows inherits its data context from the popup's owner, whenever that context arrives;
/// an open SlidePanel keeps its content in that context through a theme swap, with one drawer on the overlay.</summary>
[TestFixture]
public class SlidePanelThemeSwapTests
{
    private FakeApp _app;
    private ThemeManager _themes;

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
        _themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = _themes;
        ((FakeContext)_app.UIContext).ThemeEngine = _themes;

        var fluent = new Fluent();
        _themes.AddTheme(fluent.Name, fluent);
        var editorPro = new EditorPro();
        _themes.AddTheme(editorPro.Name, editorPro);
        _themes.SetTheme(fluent);
    }

    [Test]
    public async Task AnOpenPopupsContent_TakesTheDataContext_ThatArrivesAfterItOpened()
    {
        var model = new object();
        var content = new Border();
        var root = new Grid();
        root.Children.Add(new Popup { Child = content, IsOpen = true });
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = root };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();

        window.DataContext = model;
        await session.WaitForIdleAsync();

        Assert.That(content.DataContext, Is.SameAs(model));
    }

    [Test]
    public async Task AnOpenPanel_ThroughAThemeSwap_KeepsItsContentInTheDataContext_AndOneDrawer()
    {
        var model = new object();
        var deep = new DropDown();
        var content = new StackPanel();
        content.Children.Add(deep);
        var panel = new SlidePanel { Placement = Dock.Right, Width = 200, Content = content };
        var root = new Grid();
        root.Children.Add(panel);
        var window = new Window
        {
            Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, DataContext = model, Content = root
        };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        panel.IsOpen = true;
        await session.WaitForIdleAsync();

        _themes.SetTheme(_themes["EditorPro"]);
        await session.WaitForIdleAsync();

        Assert.That(deep.DataContext, Is.SameAs(model));
        Assert.That(Regex.Matches(await session.StateAsync(), "popup: Border #PART_Drawer").Count, Is.EqualTo(1));
    }
}
