using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A page kept between visits (<c>x:KeepAlive</c>) built in the background (<c>DeferContent</c>) while its tab is left and
/// entered again: one page is shown, and no second copy is kept beside it or put in its place.
/// </summary>
[TestFixture]
public class DeferredKeptPageTests
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
        ParkedVisuals.Clear();
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    // Left and entered again while its first build runs: the second build lands after the first one was parked, and
    // the first one is shown.
    [Test]
    public void ATabEnteredAgain_WhileItsPageIsBuilt_ShowsTheFirstPage()
    {
        using var builds = new Builds(gated: 1);
        var control = Tabs(builds);
        control.SelectedIndex = 1;
        Lay(control);
        Assume.That(builds.Started(1), Is.True, "the first build runs");

        control.SelectedIndex = -1;
        Lay(control);
        control.SelectedIndex = 1;
        Lay(control);
        builds.Open(1);
        PumpUntil(control, () => builds.Count == 2 && ShownPage(control) != null);
        PumpUntil(control, () => false, seconds: 0.3);

        var pages = builds.Pages;
        Assert.Multiple(() =>
        {
            Assert.That(pages, Has.Length.EqualTo(2), "built twice");
            Assert.That(ShownPage(control), Is.SameAs(pages[0]), "the first page is shown");
            Assert.That(pages[1].IsDiscarded, Is.True, "the second copy is let go");
            Assert.That(ParkedVisuals.Holds(control, "Two"), Is.False, "nothing kept beside it");
        });
    }

    // A build left behind lands while the tab shows the page it already took up: that page stays, the late one goes.
    [Test]
    public void APageLandingLate_WhileItsTabShowsOne_IsLetGo()
    {
        using var builds = new Builds(gated: 2);
        var control = Tabs(builds);
        control.SelectedIndex = 1;
        Lay(control);
        Assume.That(builds.Started(1), Is.True, "the first build runs");

        control.SelectedIndex = -1;
        Lay(control);
        control.SelectedIndex = 1;
        Lay(control);
        builds.Open(1);
        Assume.That(PumpUntil(control, () => ParkedVisuals.Holds(control, "Two")), Is.True, "the first page parked");
        Assume.That(builds.Started(2), Is.True, "the second build runs");

        control.SelectedIndex = -1;
        Lay(control);
        control.SelectedIndex = 1;
        Lay(control);
        var shown = ShownPage(control);
        builds.Open(2);
        PumpUntil(control, () => builds.Pages.Length == 2 && builds.Pages[1].IsDiscarded);
        PumpUntil(control, () => false, seconds: 0.3);

        var pages = builds.Pages;
        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.SameAs(pages[0]), "the first page taken up");
            Assert.That(ShownPage(control), Is.SameAs(pages[0]), "and still shown");
            Assert.That(pages[1].IsDiscarded, Is.True, "the late copy is let go");
            Assert.That(ParkedVisuals.Holds(control, "Two"), Is.False, "nothing kept beside it");
        });
    }

    // Thrown away - its window closed - while its page is built: the page landing late is let go, not hosted by the
    // presenter that went, where nothing would ever let it go.
    [Test]
    public void APageLandingAfterItsTabsWentAway_IsLetGo()
    {
        using var builds = new Builds(gated: 1);
        var control = Tabs(builds);
        control.SelectedIndex = 1;
        Lay(control);
        Assume.That(builds.Started(1), Is.True, "the build runs");

        DiscardedVisuals.PublishSubtree(control);
        while (DiscardedVisuals.Drain(int.MaxValue) > 0)
        {
        }

        builds.Open(1);
        PumpUntil(control, () => builds.Pages.Length == 1 && builds.Pages[0].IsDiscarded);

        var page = builds.Pages.Single();
        Assert.Multiple(() =>
        {
            Assert.That(page.IsDiscarded, Is.True, "let go");
            Assert.That(page.VisualParent, Is.Null, "hosted nowhere");
            Assert.That(ParkedVisuals.Holds(control, "Two"), Is.False, "parked nowhere");
        });
    }

    // A header and a body showing the same content through templates of their own: through a theme swap each shows its
    // own again - the header does not take the body's page.
    [Test]
    public void AHeaderShowingTheSameContent_DoesNotTakeTheBodysPage()
    {
        var content = new object();
        var control = new Expander
        {
            IsExpanded = true,
            Header = content,
            Content = content,
            HeaderTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new Adamantium.UI.Controls.Text.TextBlock { Text = "Header" } }),
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
        };
        Lay(control);
        var page = Shown(control, "PART_ContentPresenter");
        Assume.That(page, Is.Not.Null, "the body shows the page");

        var graphite = new Adamantium.UI.Themes.GraphiteTheme.Graphite();
        _app.ThemeManager.AddTheme(graphite.Name, graphite);
        _app.ThemeManager.SetTheme(graphite);
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(Shown(control, "PART_HeaderPresenter"), Is.Null, "the header shows no page");
            Assert.That(Shown(control, "PART_ContentPresenter"), Is.SameAs(page), "the body shows its page");
        });
    }

    private static TabControl Tabs(Builds builds) => new()
    {
        DeferContent = true,
        ItemsSource = new[] { "One", "Two" },
        ContentTemplate = new DataTemplate(builds.Build),
    };

    private static KeptPage ShownPage(TabControl control) => Shown(control, "PART_SelectedContentHost");

    private static KeptPage Shown(TemplatedUIComponent control, string part) =>
        (control.GetTemplateChild(part) as IUIComponent)?.VisualChildren.OfType<KeptPage>().FirstOrDefault();

    private static bool PumpUntil(MeasurableUIComponent control, Func<bool> done, double seconds = 5)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < seconds)
        {
            LoopSignal.Drain();
            Lay(control);
            if (done())
            {
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    private static void Lay(MeasurableUIComponent control)
    {
        control.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        control.Measure(new Size<double>(600, 400), force: true);
        control.Arrange(new Rect(0, 0, 600, 400));
    }
}
