using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Buttons;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.GraphiteTheme;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.MacOsTheme;
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
        _app.Windows = [];
        Adamantium.UI.Core.Media.Animation.AnimationManager.Reset();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        _themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = _themes;
        ((FakeContext)_app.UIContext).ThemeEngine = _themes;

        var fluent = new Fluent();
        _themes.AddTheme(fluent.Name, fluent);
        var graphite = new Graphite();
        _themes.AddTheme(graphite.Name, graphite);
        var macOs = new MacOs();
        _themes.AddTheme(macOs.Name, macOs);
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

        _themes.SetTheme(_themes["Graphite"]);
        await session.WaitForIdleAsync();

        Assert.That(deep.DataContext, Is.SameAs(model));
        Assert.That(Regex.Matches(await session.StateAsync(), "popup: Border #PART_Drawer").Count, Is.EqualTo(1));
    }

    // Through a theme swap the window's content stays alive: a panel bound to the view-model still follows it.
    [TestCase(true)]
    [TestCase(false)]
    public async Task APanelBoundToTheViewModel_FollowsItAfterAThemeSwap(bool openDuringSwap)
    {
        var model = new OpenModel { IsOpen = openDuringSwap };
        var panel = new SlidePanel { Placement = Dock.Right, Width = 200, Content = new StackPanel() };
        panel.SetBinding(SlidePanel.IsOpenProperty, new Adamantium.UI.Core.Data.Binding(nameof(OpenModel.IsOpen))
        {
            Mode = Adamantium.UI.Core.Data.BindingMode.TwoWay,
        });
        var root = new Grid();
        root.Children.Add(panel);
        var window = new Window
        {
            Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, DataContext = model, Content = root
        };
        _app.Windows = [window];
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        var lostTheContext = false;
        panel.DataContextChanged += (_, e) => lostTheContext |= e.NewValue == null;

        _themes.SetTheme(_themes["Graphite"]);
        for (var frame = 0; frame < 100 && _themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            await session.WaitForIdleAsync();
        }

        model.IsOpen = !openDuringSwap;
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(root.IsDiscarded, Is.False, "the window's content");
            Assert.That(panel.IsDiscarded, Is.False, "the panel");
            Assert.That(lostTheContext, Is.False, "the panel kept the view-model through the swap");
            Assert.That(panel.IsOpen, Is.EqualTo(!openDuringSwap), "the panel follows the view-model");
        });
    }

    [TestCase(true, TestName = "APageShownBefore_TakesTheNewThemesTemplates_WhenItsTabIsSelectedAfterASwap")]
    [TestCase(false, TestName = "APageNeverShown_TakesTheNewThemesTemplates_WhenItsTabIsSelectedAfterASwap")]
    public async Task APageOfATabNotSelected_TakesTheNewThemesTemplates_WhenSelectedAfterASwap(bool shownBefore)
    {
        var onPage = new Button();
        var inWindow = new Button();
        var page = new StackPanel();
        page.Children.Add(onPage);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "First", Content = new Border() });
        tabs.Items.Add(new TabItem { Header = "Second", Content = page });
        var root = new Grid();
        root.Children.Add(inWindow);
        root.Children.Add(tabs);
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = root };
        _app.Windows = [window];
        var session = AutomationSession.InProcess(window);
        if (shownBefore)
        {
            tabs.SelectedIndex = 1;
            await session.WaitForIdleAsync();
        }

        tabs.SelectedIndex = 0;
        await session.WaitForIdleAsync();
        var fluentTemplate = inWindow.Template;
        Assert.That(onPage.Template, Is.SameAs(fluentTemplate));

        _themes.SetTheme(_themes["macOS"]);
        for (var frame = 0; frame < 100 && _themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            await session.WaitForIdleAsync();
        }

        tabs.SelectedIndex = 1;
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(inWindow.Template, Is.Not.SameAs(fluentTemplate), "the window's button took the new theme's template");
            Assert.That(onPage.Template, Is.SameAs(inWindow.Template), "the page's button did too");
        });
    }

    [TestCase(true, TestName = "AnOpenPanelsContent_TakesTheNewThemesTemplates_InASwap")]
    [TestCase(false, TestName = "AClosedPanelsContent_TakesTheNewThemesTemplates_WhenItOpensAfterASwap")]
    public async Task APanelsContent_WearsTheThemeTheWindowWears(bool openDuringSwap)
    {
        var inPanel = new Button();
        var inWindow = new Button();
        var content = new StackPanel();
        content.Children.Add(inPanel);
        var panel = new SlidePanel { Placement = Dock.Right, Width = 200, Content = content };
        var root = new Grid();
        root.Children.Add(inWindow);
        root.Children.Add(panel);
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = root };
        _app.Windows = [window];
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        panel.IsOpen = true;
        await session.WaitForIdleAsync();
        panel.IsOpen = openDuringSwap;
        await session.WaitForIdleAsync();
        var fluentTemplate = inWindow.Template;
        Assert.That(inPanel.Template, Is.SameAs(fluentTemplate));

        _themes.SetTheme(_themes["macOS"]);
        for (var frame = 0; frame < 100 && _themes.IsThemeChanging; frame++)
        {
            Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
            await session.WaitForIdleAsync();
        }

        panel.IsOpen = true;
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(inWindow.Template, Is.Not.SameAs(fluentTemplate), "the window's button took the new theme's template");
            Assert.That(inPanel.Template, Is.SameAs(inWindow.Template), "the panel's button did too");
        });
    }
}
