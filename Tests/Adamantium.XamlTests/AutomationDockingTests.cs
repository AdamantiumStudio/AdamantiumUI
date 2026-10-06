using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Navigation;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Automation;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Docking through automation: a pane goes to an edge, into the documents, beside another pane's panel or into it as a
/// tab, by the layout's rules - and a panel carries every pane in it.
/// </summary>
[TestFixture]
public class AutomationDockingTests
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

    private static PaneGroup Group(DockZone zone, PaneKind kind, params string[] panes)
    {
        var group = new PaneGroup { Zone = zone };
        foreach (var pane in panes)
        {
            group.Items.Add(new Pane { Header = pane, Id = pane, Kind = kind });
        }

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

    [Test]
    public async Task APane_GoesToAnEdge_IntoTheDocuments_AndSaysWhereItIs()
    {
        var (session, area) = await Driving();
        await using var _ = session;

        var was = await Tab(session, "Console").GetAsync();
        await Tab(session, "Console").DockAsync(DockPosition.Left);
        var left = await Tab(session, "Console").GetAsync();
        await Tab(session, "Console").DockAsync(DockPosition.Fill);

        Assert.Multiple(() =>
        {
            Assert.That(was.DockPosition, Is.EqualTo("Bottom"));
            Assert.That(left.DockPosition, Is.EqualTo("Left"));
            Assert.That(area.PaneById("Console").Zone, Is.EqualTo(DockZone.Center));
            Assert.That(area.Layout.FindGroup("Console"), Is.SameAs(area.Layout.FindGroup("Scene")),
                "into the documents is a tab beside the documents");
        });
    }

    [Test]
    public async Task APane_DocksBesideAnothersPanel_OrIntoItAsATab_AndARefusedPlaceIsSaid()
    {
        var (session, area) = await Driving();
        await using var _ = session;

        await Tab(session, "Hierarchy").DockAsync(DockPosition.Bottom, Tab(session, "Inspector"));
        var split = area.Layout.FindGroup("Hierarchy") != area.Layout.FindGroup("Inspector");
        await Tab(session, "Console").DockAsync(DockPosition.Fill, Tab(session, "Inspector"));
        area.PaneById("Game").Allowed = DockZone.Center;
        var refused = Assert.ThrowsAsync<AutomationException>(() => Tab(session, "Game").DockAsync(DockPosition.Left));

        Assert.Multiple(() =>
        {
            Assert.That(split, Is.True, "beside a panel is a panel of its own");
            Assert.That(area.Layout.FindGroup("Console"), Is.SameAs(area.Layout.FindGroup("Inspector")));
            Assert.That(refused.Message, Does.Contain("may not go to Left"));
            Assert.That(area.PaneById("Game").Zone, Is.EqualTo(DockZone.Center), "a refused move leaves it where it was");
        });
    }

    [Test]
    public async Task APanel_CarriesEveryPaneInIt()
    {
        var (session, area) = await Driving();
        await using var _ = session;
        var panel = Tab(session, "Inspector").Parent();
        var was = await panel.GetAsync();

        await panel.DockAsync(DockPosition.Left);

        Assert.Multiple(() =>
        {
            Assert.That(was.ControlType, Is.EqualTo("Tab"));
            Assert.That(was.DockPosition, Is.EqualTo("Right"));
            Assert.That(area.PaneById("Inspector").Zone, Is.EqualTo(DockZone.Left));
            Assert.That(area.PaneById("Hierarchy").Zone, Is.EqualTo(DockZone.Left));
            Assert.That(area.Layout.FindGroup("Inspector"), Is.SameAs(area.Layout.FindGroup("Hierarchy")));
        });
    }
}
