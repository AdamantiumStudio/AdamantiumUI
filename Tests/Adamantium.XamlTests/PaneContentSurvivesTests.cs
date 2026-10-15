using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A pane's body is the pane's own: the panel showing it only lends it a place. Switching tabs, or moving a pane to another
/// panel and back, must find the body alive - it came back on screen empty, its labels gone.
/// </summary>
[TestFixture]
public class PaneContentSurvivesTests
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

    private static Pane Pane(string name, PaneKind kind) => new()
    {
        Header = name,
        Id = name,
        Kind = kind,
        Content = new StackPanel { Children = { new TextBlock { Text = name }, new CheckBox { Content = name + " option" } } }
    };

    private static PaneGroup Group(DockZone zone, PaneKind kind, params string[] panes)
    {
        var group = new PaneGroup { Zone = zone };
        foreach (var pane in panes) group.Items.Add(Pane(pane, kind));
        return group;
    }

    private static async Task<(AutomationSession Session, DockingArea Area)> Driving()
    {
        var area = new DockingArea
        {
            Children =
            {
                Group(DockZone.Center, PaneKind.Document, "Scene", "Game"),
                Group(DockZone.Right, PaneKind.Tool, "Inspector", "Hierarchy"),
                Group(DockZone.Bottom, PaneKind.Tool, "Console")
            }
        };
        var window = new Window { Width = 1000, Height = 700, ClientWidth = 1000, ClientHeight = 700, Content = area };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return (session, area);
    }

    private static AutomationElement Tab(AutomationSession session, string name) =>
        session.Find(By.Type(AutomationControlType.TabItem).And(By.Name(name)));

    private static void AssertShownAlive(DockingArea area, string id)
    {
        var body = (StackPanel)area.PaneById(id).Content;
        var option = (CheckBox)body.Children[1];

        Assert.Multiple(() =>
        {
            Assert.That(body.IsDiscarded, Is.False, $"{id}: its body was thrown away");
            Assert.That(body.VisualParent, Is.Not.Null, $"{id}: its body is not shown");
            Assert.That(body.RootVisual, Is.Not.Null, $"{id}: its body is out of the window");
            Assert.That(option.IsDiscarded, Is.False, $"{id}: its option was thrown away");
            Assert.That(option.VisualChildren, Is.Not.Empty, $"{id}: its option lost its parts");
        });
    }

    // A tool panel takes its own template once it learns its kind: the first template's content host, measured after it
    // was thrown away, took the body from the second one.
    [Test]
    public async Task EveryPanelShowsItsFrontPane()
    {
        var (session, area) = await Driving();
        await using var _ = session;

        AssertShownAlive(area, "Scene");
        AssertShownAlive(area, "Inspector");
        AssertShownAlive(area, "Console");
    }

    [Test]
    public async Task ATabLeftAndComeBackTo_ShowsItsBodyAlive()
    {
        var (session, area) = await Driving();
        await using var _ = session;

        await Tab(session, "Game").SelectAsync();
        await Tab(session, "Hierarchy").SelectAsync();
        await session.WaitForIdleAsync();
        await Tab(session, "Scene").SelectAsync();
        await Tab(session, "Inspector").SelectAsync();
        await session.WaitForIdleAsync();

        AssertShownAlive(area, "Scene");
        AssertShownAlive(area, "Inspector");
    }

    private static async Task<(AutomationSession Session, StackPanel First, StackPanel Second)> PlainTabs()
    {
        var first = new StackPanel { Children = { new TextBlock { Text = "First" }, new CheckBox { Content = "First option" } } };
        var second = new StackPanel { Children = { new TextBlock { Text = "Second" }, new CheckBox { Content = "Second option" } } };
        var tabs = new TabControl
        {
            ShowCloseButton = true,
            Items = { new TabItem { Header = "First", Content = first }, new TabItem { Header = "Second", Content = second } }
        };
        var window = new Window { Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, Content = tabs };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return (session, first, second);
    }

    [Test]
    public async Task APlainTabLeftAndComeBackTo_ShowsItsPageAlive()
    {
        var (session, first, _) = await PlainTabs();
        await using var _ = session;

        await Tab(session, "Second").SelectAsync();
        await session.WaitForIdleAsync();
        await Tab(session, "First").SelectAsync();
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.IsDiscarded, Is.False);
            Assert.That(first.RootVisual, Is.Not.Null);
            Assert.That(((CheckBox)first.Children[1]).IsDiscarded, Is.False);
        });
    }

    [Test]
    public async Task AClosedPlainTab_LetsItsPageGo()
    {
        var (session, first, second) = await PlainTabs();
        await using var _ = session;

        await Tab(session, "First").Find(By.Id("CloseTab")).InvokeAsync();
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.IsDiscarded, Is.True, "the closed tab's page");
            Assert.That(second.IsDiscarded, Is.False, "the tab left open");
            Assert.That(second.RootVisual, Is.Not.Null);
        });
    }

    // The other side: a body nobody will show again still goes.
    [Test]
    public async Task AClosedDocument_LetsItsBodyGo()
    {
        var (session, area) = await Driving();
        await using var _ = session;
        var body = (StackPanel)area.PaneById("Scene").Content;

        await area.ClosePaneAsync("Scene");
        await session.WaitForIdleAsync();

        Assert.That(body.IsDiscarded, Is.True);
    }

    [Test]
    public async Task APaneMovedToAnotherPanelAndBack_ShowsItsBodyAlive()
    {
        var (session, area) = await Driving();
        await using var _ = session;

        await Tab(session, "Scene").DockAsync(DockPosition.Fill, Tab(session, "Inspector"));
        await session.WaitForIdleAsync();
        await Tab(session, "Scene").SelectAsync();
        await session.WaitForIdleAsync();
        AssertShownAlive(area, "Scene");

        await Tab(session, "Scene").DockAsync(DockPosition.Fill, Tab(session, "Game"));
        await session.WaitForIdleAsync();
        await Tab(session, "Scene").SelectAsync();
        await session.WaitForIdleAsync();

        AssertShownAlive(area, "Scene");
    }
}
