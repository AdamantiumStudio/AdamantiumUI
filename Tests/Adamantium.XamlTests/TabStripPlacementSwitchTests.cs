using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Resources.Triggers;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// The tabs move with the strip when it moves to another side: the theme's Top strip holds them in two lists of its
/// own (pinned and the rest), the other sides in the control's single presenter.
/// </summary>
[TestFixture]
public class TabStripPlacementSwitchTests
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

    [TestCase(TabStripPlacement.Top, TabStripPlacement.Left)]
    [TestCase(TabStripPlacement.Top, TabStripPlacement.Right)]
    [TestCase(TabStripPlacement.Top, TabStripPlacement.Bottom)]
    [TestCase(TabStripPlacement.Left, TabStripPlacement.Top)]
    [TestCase(TabStripPlacement.Left, TabStripPlacement.Right)]
    public void TheTabs_MoveWithTheStrip(TabStripPlacement from, TabStripPlacement to)
    {
        var control = new TabControl { TabStripPlacement = from, ItemsSource = new[] { "One", "Two", "Three" } };
        control.SelectedIndex = 0;
        Lay(control);
        Assume.That(control.ItemsHostPanel?.VisualChildren.Count(), Is.EqualTo(3), "three tabs before the move");

        control.TabStripPlacement = to;
        Lay(control);

        Assert.That(control.ItemsHostPanel?.VisualChildren.OfType<TabItem>().Count(), Is.EqualTo(3),
            "the three tabs stand in the moved strip");
    }

    [Test]
    public void TheTabs_MoveWithTheStripRoundEverySide()
    {
        var control = new TabControl { TabStripPlacement = TabStripPlacement.Left, ItemsSource = new[] { "One", "Two", "Three" } };
        control.SelectedIndex = 0;
        Lay(control);

        foreach (var side in new[] { TabStripPlacement.Top, TabStripPlacement.Right, TabStripPlacement.Bottom, TabStripPlacement.Left })
        {
            control.TabStripPlacement = side;
            Lay(control);
            Assert.That(control.ItemsHostPanel?.VisualChildren.OfType<TabItem>().Count(), Is.EqualTo(3), side.ToString());
        }
    }

    // A strip of its own on a side, BasedOn the theme's: the theme's template for that side applies first, then the
    // derived one - and the tabs must end up in the derived one's presenter.
    [TestCase(TabStripPlacement.Left)]
    [TestCase(TabStripPlacement.Right)]
    [TestCase(TabStripPlacement.Bottom)]
    public void TheTabs_MoveIntoADerivedStripsOwnTemplate(TabStripPlacement side)
    {
        _app.ThemeManager.AddStyleSet<DerivedStripStyles>();
        var control = new DerivedStrip { ItemsSource = new[] { "One", "Two", "Three" } };
        control.SelectedIndex = 0;
        Lay(control);
        Assume.That(control.ItemsHostPanel?.VisualChildren.Count(), Is.EqualTo(3), "three tabs on top");

        control.TabStripPlacement = side;
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(control.GetTemplateChild("DerivedStack"), Is.Not.Null, "the derived template is the one applied");
            Assert.That(control.ItemsHostPanel?.VisualChildren.OfType<TabItem>().Count(), Is.EqualTo(3),
                "the three tabs stand in the derived strip");
        });
    }

    // Tabs written in place are their own containers: they outlive the template that showed them, so taking that
    // template down must leave them whole - their own parts still theirs - for the next one.
    [TestCase(TabStripPlacement.Left)]
    [TestCase(TabStripPlacement.Bottom)]
    public void TabsWrittenInPlace_MoveWhole(TabStripPlacement side)
    {
        _app.ThemeManager.AddStyleSet<DerivedStripStyles>();
        var control = new DerivedStrip();
        foreach (var header in new[] { "One", "Two", "Three" })
        {
            control.Items.Add(new TabItem { Header = header });
        }

        control.SelectedIndex = 0;
        Lay(control);

        control.TabStripPlacement = side;
        Lay(control);

        var tabs = control.ItemsHostPanel?.VisualChildren.OfType<TabItem>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(tabs, Has.Count.EqualTo(3), "the three tabs stand in the moved strip");
            foreach (var tab in tabs ?? [])
            {
                var header = tab.GetTemplateChild("PART_ContentPresenter") as Adamantium.UI.Controls.Base.UIComponent;
                Assert.That(header, Is.Not.Null, $"{tab.Header}: its template is there");
                Assert.That(header?.TemplatedParent, Is.SameAs(tab), $"{tab.Header}: its parts are still its own");
            }
        });
    }

    // A plain list of elements of its own through a theme's template going and coming: the items are shown again,
    // their parts their own.
    [Test]
    public void ElementItems_SurviveTheTemplateGoingAndComing()
    {
        var list = new ItemsControl();
        var first = new Adamantium.UI.Controls.Buttons.Button { Content = "One" };
        list.Items.Add(first);
        list.Items.Add(new Adamantium.UI.Controls.Buttons.Button { Content = "Two" });
        list.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(list);
        list.Measure(new Size<double>(300, 300));
        var template = list.Template;

        list.Template = null;
        list.Measure(new Size<double>(300, 300), force: true);
        list.Template = template;
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(list);
        list.Measure(new Size<double>(300, 300), force: true);

        Assert.Multiple(() =>
        {
            Assert.That(list.ItemsHostPanel?.VisualChildren.Count(), Is.EqualTo(2), "both items shown again");
            var part = first.GetTemplateChild("InnerBorder") as Adamantium.UI.Controls.Base.UIComponent;
            Assert.That(part?.TemplatedParent, Is.SameAs(first), "the item's parts are still its own");
        });
    }

    // A page kept while its tab is away is kept while the strip moves too: the next template shows the same page, never
    // let go - a browser's page would be made again, loading from scratch with its history gone.
    [TestCase(TabStripPlacement.Top, TabStripPlacement.Left)]
    [TestCase(TabStripPlacement.Left, TabStripPlacement.Bottom)]
    public void AKeptPage_MovesWithTheStrip(TabStripPlacement from, TabStripPlacement to)
    {
        var control = new TabControl
        {
            TabStripPlacement = from,
            ItemsSource = new[] { "One", "Two" },
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
        };
        control.SelectedIndex = 1;
        Lay(control);
        var page = ShownPage(control);
        Assume.That(page, Is.Not.Null, "the page shown before the move");

        control.TabStripPlacement = to;
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(ShownPage(control), Is.SameAs(page), "the same page in the moved strip");
            Assert.That(page.IsDiscarded, Is.False, "never let go");
            Assert.That(page.IsParked, Is.False, "shown again, not left parked");
        });
    }

    // The strip moved while the page was still being built in the background: the page landing late waits with the tab
    // control, for whoever comes back to that tab - not with the presenter that went with the old template, where
    // nothing would ever let it go.
    [Test]
    public void APageBuiltLate_AfterTheStripMoved_WaitsWithTheTabControl()
    {
        using var building = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);
        var builds = 0;
        var control = new TabControl
        {
            DeferContent = true,
            ItemsSource = new[] { "One", "Two" },
            ContentTemplate = new DataTemplate(() =>
            {
                if (Interlocked.Increment(ref builds) == 1)
                {
                    building.Set();
                    gate.Wait(TimeSpan.FromSeconds(10));
                }

                return new TemplateResult { RootComponent = new KeptPage() };
            }),
        };
        control.SelectedIndex = 1;
        Lay(control);
        Assume.That(building.Wait(TimeSpan.FromSeconds(5)), Is.True, "the page is being built in the background");

        control.TabStripPlacement = TabStripPlacement.Left;
        Lay(control);
        gate.Set();
        var shown = PumpUntil(control, () => ShownPage(control) != null);
        PumpUntil(control, () => false, seconds: 0.3);

        var waited = ParkedVisuals.TryTake(control, "Two", null, out var late, out _, out _, out _);
        Assert.Multiple(() =>
        {
            Assert.That(shown, Is.True, "the moved strip shows a page");
            Assert.That(waited, Is.True, "the page built late waits with the tab control");
            Assert.That(late, Is.Not.SameAs(ShownPage(control)), "it is the late one, not the one shown");
            Assert.That((late as KeptPage)?.IsDiscarded, Is.False, "and is whole");
        });
    }

    private static bool PumpUntil(TabControl control, Func<bool> done, double seconds = 5)
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

    private static KeptPage ShownPage(TabControl control) =>
        (control.GetTemplateChild("PART_SelectedContentHost") as IUIComponent)?.VisualChildren.OfType<KeptPage>().FirstOrDefault();

    private sealed class KeptPage : Border
    {
        public override NavigationCacheMode KeepAlive => NavigationCacheMode.Required;
    }

    private sealed class DerivedStrip : TabControl
    {
    }

    private sealed class DerivedStripStyles : StyleSet
    {
        public DerivedStripStyles()
        {
            foreach (var side in new[] { "Left", "Right", "Bottom" })
            {
                var selector = new StyleSelector();
                selector.Types.Add(typeof(DerivedStrip));
                selector.Conditions.Add(new Condition { Property = nameof(TabControl.TabStripPlacement), Value = side });
                var basedOn = new StyleSelector();
                basedOn.Types.Add(typeof(TabControl));
                var style = new Style { Selector = selector, BasedOn = basedOn };
                style.Setters.Add(new Setter { Property = "Template", Value = StackedTemplate() });
                Add(style);
            }
        }

        private static ControlTemplate StackedTemplate() => new(() =>
        {
            var root = new Grid();
            var stack = new StackPanel();
            var presenter = new ItemsPresenter();
            var page = new ContentPresenter();
            stack.Children.Add(presenter);
            root.Children.Add(stack);
            root.Children.Add(page);
            var result = new TemplateResult { RootComponent = root };
            result.RegisterName("DerivedStack", stack);
            result.RegisterName("PART_ItemsPresenter", presenter);
            result.RegisterName("PART_SelectedContentHost", page);
            return result;
        });
    }

    private static void Lay(TabControl control)
    {
        control.ApplyCurrentTheme();
        Adamantium.UI.Extensions.WindowExtension.UpdateTree(control);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        control.Measure(new Size<double>(600, 400), force: true);
        control.Arrange(new Rect(0, 0, 600, 400));
    }
}
