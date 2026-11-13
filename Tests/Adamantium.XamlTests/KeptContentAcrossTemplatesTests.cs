using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Core.Templates;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.GraphiteTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// Content kept between visits (<c>x:KeepAlive</c>) stays the same element when its control takes another template - a
/// theme swapped - in every control whose template shows it through a presenter.
/// </summary>
[TestFixture]
public class KeptContentAcrossTemplatesTests
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
        Use(new Fluent());
    }

    [Test]
    public void AContentControlsKeptPage_OutlivesATheme()
    {
        var control = new ContentControl
        {
            Content = "Page",
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
        };
        Lay(control);
        var page = Shown(control, "PART_ContentPresenter");
        Assume.That(page, Is.Not.Null, "the page shown before the swap");

        Use(new Graphite());
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(Shown(control, "PART_ContentPresenter"), Is.SameAs(page), "the same page in the new template");
            Assert.That(page.IsDiscarded, Is.False, "never let go");
            Assert.That(page.IsParked, Is.False, "shown again, not left parked");
        });
    }

    [Test]
    public void ATabsKeptPage_OutlivesATheme()
    {
        var control = new TabControl
        {
            ItemsSource = new[] { "One", "Two" },
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
        };
        control.SelectedIndex = 1;
        Lay(control);
        var page = Shown(control, "PART_SelectedContentHost");
        Assume.That(page, Is.Not.Null, "the page shown before the swap");

        Use(new Graphite());
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(Shown(control, "PART_SelectedContentHost"), Is.SameAs(page), "the same page in the new template");
            Assert.That(page.IsDiscarded, Is.False, "never let go");
            Assert.That(page.IsParked, Is.False, "shown again, not left parked");
        });
    }

    // No template after: the control shows its content itself, so its kept element is shown live, not left parked.
    [Test]
    public void AKeptElement_WithNoTemplateAfter_IsShownByTheControl()
    {
        var page = new KeptPage();
        var control = new ContentControl { Content = page };
        Lay(control);
        Assume.That(Shown(control, "PART_ContentPresenter"), Is.SameAs(page), "shown by the template first");

        control.Template = null;
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(control.VisualChildren, Does.Contain(page), "the control shows it itself");
            Assert.That(page.IsParked, Is.False, "not left parked");
            Assert.That(page.IsDiscarded, Is.False, "never let go");
        });
    }

    // No template after, the page built from data: nothing will show it, so it is let go, not parked for good.
    [Test]
    public void AKeptPageBuiltFromData_WithNoTemplateAfter_IsLetGo()
    {
        var control = new ContentControl
        {
            Content = "Page",
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
        };
        Lay(control);
        var page = Shown(control, "PART_ContentPresenter");
        Assume.That(page, Is.Not.Null, "the page shown first");

        control.Template = null;
        Lay(control);
        while (DiscardedVisuals.Drain(int.MaxValue) > 0)
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(ParkedVisuals.TryTake(control, "Page", null, out _, out _, out _, out _), Is.False,
                "nothing left parked");
            Assert.That(page.IsDiscarded, Is.True, "let go");
            Assert.That(page.VisualParent, Is.Null, "the presenter that went holds it no more");
        });
    }

    [Test]
    public void AScrollViewersKeptPage_OutlivesATheme()
    {
        var control = new ScrollViewer
        {
            Content = "Page",
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
        };
        Lay(control);
        var page = Shown(control, "PART_ScrollContentPresenter");
        Assume.That(page, Is.Not.Null, "the page shown before the swap");

        Use(new Graphite());
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(Shown(control, "PART_ScrollContentPresenter"), Is.SameAs(page), "the same page in the new template");
            Assert.That(page.IsParked, Is.False, "shown again, not left parked");
        });
    }

    // Collapsed through a theme swap, the next template is held back; taken away before it is built, the control shows
    // its content itself - the element parked for that template is no longer parked.
    [Test]
    public void AKeptElement_ParkedForATemplateNeverBuilt_IsShownByTheControl()
    {
        var page = new KeptPage();
        var control = new ContentControl { Content = page };
        Lay(control);
        Assume.That(Shown(control, "PART_ContentPresenter"), Is.SameAs(page), "shown by the template first");

        control.Visibility = Visibility.Collapsed;
        Use(new Graphite());
        Lay(control);
        Assume.That(control.GetTemplateChild("PART_ContentPresenter"), Is.Null, "the new template held back");
        control.Template = null;
        control.Visibility = Visibility.Visible;
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(control.VisualChildren, Does.Contain(page), "the control shows it itself");
            Assert.That(page.IsParked, Is.False, "not left parked");
            Assert.That(ParkedVisuals.TryTake(control, page, null, out _, out _, out _, out _), Is.False,
                "nothing waits for it");
        });
    }

    [Test]
    public void AKeptPageBuiltFromData_ParkedForATemplateNeverBuilt_IsLetGo()
    {
        var control = new ContentControl
        {
            Content = "Page",
            ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
        };
        Lay(control);
        var page = Shown(control, "PART_ContentPresenter");
        Assume.That(page, Is.Not.Null, "the page shown first");

        control.Visibility = Visibility.Collapsed;
        Use(new Graphite());
        Lay(control);
        control.Template = null;
        control.Visibility = Visibility.Visible;
        Lay(control);

        Assert.Multiple(() =>
        {
            Assert.That(ParkedVisuals.TryTake(control, "Page", null, out _, out _, out _, out _), Is.False,
                "nothing left parked");
            Assert.That(page.IsDiscarded, Is.True, "let go");
        });
    }

    // A content control that is itself a part of another control's template goes with that template: what it showed is
    // not parked under it, since nothing will ask it for that again.
    [Test]
    public void APartsKeptPage_IsNotParkedUnderThePartThatWent()
    {
        ContentControl inner = null;
        var outer = new ContentControl
        {
            Template = new ControlTemplate(() =>
            {
                inner = new ContentControl
                {
                    Content = "Page",
                    ContentTemplate = new DataTemplate(() => new TemplateResult { RootComponent = new KeptPage() }),
                };
                return new TemplateResult { RootComponent = inner };
            }),
        };
        Lay(outer);
        Assume.That(Shown(inner, "PART_ContentPresenter"), Is.Not.Null, "the part shows the page");
        var part = inner;

        outer.Template = new ControlTemplate(() => new TemplateResult { RootComponent = new Adamantium.UI.Controls.Decorators.Border() });
        Lay(outer);

        Assert.That(ParkedVisuals.TryTake(part, "Page", null, out _, out _, out _, out _), Is.False,
            "nothing parked under the part that went");
    }

    private static KeptPage Shown(TemplatedUIComponent control, string part) =>
        (control.GetTemplateChild(part) as IUIComponent)?.VisualChildren.OfType<KeptPage>().FirstOrDefault();

    private void Use(Theme theme)
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
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
