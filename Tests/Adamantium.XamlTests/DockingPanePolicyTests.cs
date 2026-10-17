using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.Navigation;
using Adamantium.ProceduralGeometry;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Docking;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// The room around a panel's body and the side a tool's selection bar runs along are the area's to say, without touching
/// the theme: unset, the theme's look stands.
/// </summary>
[TestFixture]
public class DockingPanePolicyTests
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
    public async Task Unset_TheThemesLookStands_AToolsBarUnderItsTabs()
    {
        var (session, area) = await Docked();
        var (document, tool) = Groups(area);

        Assert.Multiple(() =>
        {
            Assert.That(document.Padding, Is.EqualTo(new Thickness(12)), "Fluent's content padding");
            Assert.That(tool.SelectionIndicatorPlacement, Is.EqualTo(TabIndicatorPlacement.Outer));
            Assert.That(BarOf(tool).VerticalAlignment, Is.EqualTo(VerticalAlignment.Bottom), "under the tabs");
        });
    }

    [Test]
    public async Task TheAreaSays_EveryPanelTakesThePadding_AndOnlyToolsTheirBar()
    {
        var (session, area) = await Docked();
        var (document, tool) = Groups(area);
        var documentPlacement = document.SelectionIndicatorPlacement;

        area.PanePadding = new Thickness(0);
        area.ToolSelectionIndicatorPlacement = TabIndicatorPlacement.Inner;
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(document.Padding, Is.EqualTo(new Thickness(0)));
            Assert.That(tool.Padding, Is.EqualTo(new Thickness(0)));
            Assert.That(tool.SelectionIndicatorPlacement, Is.EqualTo(TabIndicatorPlacement.Inner));
            Assert.That(BarOf(tool).VerticalAlignment, Is.EqualTo(VerticalAlignment.Top), "beside the body above the strip");
            Assert.That(document.SelectionIndicatorPlacement, Is.EqualTo(documentPlacement), "documents keep theirs");
        });
    }

    // A tool's tabs sit under its body: rounded on top, where they join it, each left a notch at either side.
    [Test]
    public async Task AToolsTabsRoundTheirOuterCorners_NotThoseAgainstTheBody()
    {
        var (_, area) = await Docked();
        var (document, tool) = Groups(area);

        Assert.Multiple(() =>
        {
            Assert.That(TabOf(tool).CornerRadius, Is.EqualTo(new CornerRadius(0, 0, 4, 4)));
            Assert.That(TabOf(document).StripPlacement, Is.EqualTo(TabStripPlacement.Top), "documents keep the top pair");
        });
    }

    // Taken back, the area's word leaves the theme's: an unset padding is the theme's again, not the last one given.
    [Test]
    public async Task ThePaddingUnsetAgain_IsTheThemesAgain()
    {
        var (session, area) = await Docked();
        var (document, _) = Groups(area);

        area.PanePadding = new Thickness(0);
        await session.WaitForIdleAsync();
        area.PanePadding = null;
        await session.WaitForIdleAsync();

        Assert.That(document.Padding, Is.EqualTo(new Thickness(12)));
    }

    // The one stretched tab has no corners whatever side its strip is on, and the corners follow a strip moved later.
    [Test]
    public async Task TheStretchedTabHasNoCorners_AndTheCornersFollowTheStrip()
    {
        var tabs = new TabControl { StretchSingleTab = true, TabStripPlacement = TabStripPlacement.Bottom };
        tabs.Items.Add(new TabItem { Header = "One", Content = new Border() });
        var window = new Window { Width = 600, Height = 400, ClientWidth = 600, ClientHeight = 400, Content = tabs };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        var stretched = TabOf(tabs).CornerRadius;

        tabs.StretchSingleTab = false;
        await session.WaitForIdleAsync();
        var bottom = TabOf(tabs).CornerRadius;
        tabs.TabStripPlacement = TabStripPlacement.Top;
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stretched, Is.EqualTo(new CornerRadius(0)));
            Assert.That(bottom, Is.EqualTo(new CornerRadius(0, 0, 4, 4)));
            Assert.That(TabOf(tabs).CornerRadius, Is.EqualTo(new CornerRadius(4, 4, 0, 0)));
        });
    }

    private static TabItem TabOf(TabControl group)
    {
        var pending = new Stack<IUIComponent>([group]);
        while (pending.Count > 0)
        {
            var at = pending.Pop();
            if (at is TabItem tab)
            {
                return tab;
            }

            foreach (var child in at.VisualChildren)
            {
                pending.Push(child);
            }
        }

        return null;
    }

    private static async Task<(AutomationSession Session, DockingArea Area)> Docked()
    {
        var area = new DockingArea
        {
            Children =
            {
                Group(DockZone.Center, PaneKind.Document, "Scene"),
                Group(DockZone.Right, PaneKind.Tool, "Inspector", "Hierarchy")
            }
        };
        var window = new Window { Width = 1000, Height = 700, ClientWidth = 1000, ClientHeight = 700, Content = area };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return (session, area);
    }

    private static PaneGroup Group(DockZone zone, PaneKind kind, params string[] panes)
    {
        var group = new PaneGroup { Zone = zone };
        foreach (var pane in panes)
        {
            group.Items.Add(new Pane { Header = pane, Id = pane, Kind = kind, Content = new Border() });
        }

        return group;
    }

    private static (PaneGroup Document, PaneGroup Tool) Groups(DockingArea area)
    {
        PaneGroup document = null;
        PaneGroup tool = null;
        var pending = new Stack<IUIComponent>([area]);
        while (pending.Count > 0)
        {
            var at = pending.Pop();
            if (at is PaneGroup group)
            {
                if (group.Kind == PaneKind.Document)
                {
                    document ??= group;
                }
                else
                {
                    tool ??= group;
                }
            }

            foreach (var child in at.VisualChildren)
            {
                pending.Push(child);
            }
        }

        return (document, tool);
    }

    private static MeasurableUIComponent BarOf(PaneGroup group) =>
        (MeasurableUIComponent)group.GetTemplateChild("PART_SelectionIndicator");
}
