using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.GraphiteTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>What a control's template only shows keeps its data context through a theme swap, wherever in the template it
/// is shown: the old template gives it back to what holds it before the template goes, so nothing in it lets go of its
/// view-model and takes it again.</summary>
[TestFixture]
public class ThemeSwapKeepsContextTests
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
        _themes.SetTheme(fluent);
    }

    [Test]
    public async Task ThePageOfATab_KeepsItsDataContext_ThroughASwap()
    {
        var watcher = new Border();
        var page = new StackPanel();
        page.Children.Add(watcher);
        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Page", Content = page });

        var heard = await SwapUnder(tabs, watcher);

        Assert.That(heard, Is.Empty);
    }

    [Test]
    public async Task ContentShownInsideANestedPart_KeepsItsDataContext_ThroughASwap()
    {
        var watcher = new Border { Width = 50, Height = 50 };
        var zoom = new ZoomBox { Content = watcher };

        var heard = await SwapUnder(zoom, watcher);

        Assert.That(heard, Is.Empty);
    }

    [Test]
    public async Task TheContentOfAnOpenPanel_KeepsItsDataContext_ThroughASwap()
    {
        var watcher = new Border();
        var content = new StackPanel();
        content.Children.Add(watcher);
        var panel = new SlidePanel { Placement = Dock.Right, Width = 200, Content = content, IsOpen = true };
        var root = new Grid();
        root.Children.Add(panel);

        var heard = await SwapUnder(root, watcher);

        Assert.That(heard, Is.Empty);
    }

    [Test]
    public async Task ATabAndItsPage_KeepTheirDataContext_ThroughTwoSwaps()
    {
        var watcher = new Border();
        var page = new StackPanel();
        page.Children.Add(watcher);
        var tab = new TabItem { Header = "Page", Content = page };
        var tabs = new TabControl();
        tabs.Items.Add(tab);

        var heard = await SwapUnder(tabs, ["Graphite", "Fluent"], tab, watcher);

        Assert.That(heard, Is.Empty);
    }

    private Task<List<object>> SwapUnder(IUIComponent content, FundamentalUIComponent watcher) =>
        SwapUnder(content, ["Graphite"], watcher);

    private async Task<List<object>> SwapUnder(IUIComponent content, string[] themes,
        params FundamentalUIComponent[] watchers)
    {
        var model = new object();
        var window = new Window
        {
            Width = 800, Height = 600, ClientWidth = 800, ClientHeight = 600, DataContext = model, Content = content
        };
        _app.Windows = [window];
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();

        var heard = new List<object>();
        foreach (var watcher in watchers)
        {
            Assert.That(watcher.DataContext, Is.SameAs(model));
            watcher.DataContextChanged += (_, e) => heard.Add($"{watcher.GetType().Name}: {e.NewValue ?? "null"}");
        }

        foreach (var theme in themes)
        {
            _themes.SetTheme(_themes[theme]);
            for (var frame = 0; frame < 100 && _themes.IsThemeChanging; frame++)
            {
                Adamantium.UI.Core.Media.Animation.AnimationManager.Tick(0.05);
                await session.WaitForIdleAsync();
            }
        }

        foreach (var watcher in watchers)
        {
            Assert.That(watcher.DataContext, Is.SameAs(model));
        }

        return heard;
    }
}
