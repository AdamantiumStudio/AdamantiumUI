using System.Linq;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Text;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Core.Resources;
using Adamantium.Mathematics;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// A selected tab's label color follows the trigger: the presenter's Foreground reaches the TextBlock it generates for a
// string header.
[TestFixture]
public class TabForegroundFollowsSelectionTests
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
    }

    private void Use(ITheme theme)
    {
        _themes.AddTheme(theme.Name, theme);
        _themes.SetTheme(theme);
    }

    private static ITheme Build(string name) => name == "Graphite"
        ? new Adamantium.UI.Themes.GraphiteTheme.Graphite()
        : new Adamantium.UI.Themes.FluentTheme.Fluent();

    // A themed tab with its template built and its label generated - the generated TextBlock is made in the presenter's
    // MEASURE, so nothing here exists until the tab has been measured once.
    private static TabItem LiveTab()
    {
        var tab = new TabItem { Header = "Shapes" };
        tab.ApplyCurrentTheme();
        Frame(tab);
        return tab;
    }

    // What a frame does, to the extent this seam depends on it: drain the coalesced binding updates, then lay out.
    private static void Frame(TabItem tab)
    {
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        tab.Measure(new Size(200, 32));
    }

    private static ContentPresenter PresenterOf(TabItem tab)
    {
        var presenter = tab.GetTemplateChild("PART_ContentPresenter") as ContentPresenter;
        Assert.That(presenter, Is.Not.Null, "the tab template has no PART_ContentPresenter");
        return presenter;
    }

    private static TextBlock LabelOf(TabItem tab) =>
        PresenterOf(tab).VisualChildren.OfType<TextBlock>().FirstOrDefault();

    private static Color ColorOf(Brush brush) =>
        brush is SolidColorBrush solid ? solid.Color : default;

    /// <summary>The first link: the TRIGGER onto the part. If this one holds and the label still does not change, the
    /// defect is in the hand-off from the presenter to the text it generated, not in the theme.</summary>
    [TestCase("Graphite")]
    [TestCase("Fluent")]
    public void SelectingATab_ChangesThePresentersForeground(string themeName)
    {
        Use(Build(themeName));
        var tab = LiveTab();
        var presenter = PresenterOf(tab);
        var resting = ColorOf(presenter.Foreground);

        tab.IsSelected = true;

        Assert.That(ColorOf(presenter.Foreground), Is.Not.EqualTo(resting),
            "the IsSelected trigger never reached PART_ContentPresenter");
    }

    [TestCase("Graphite")]
    [TestCase("Fluent")]
    public void SelectingATab_ChangesItsLabelColor(string themeName)
    {
        Use(Build(themeName));
        var tab = LiveTab();
        var label = LabelOf(tab);
        Assert.That(label, Is.Not.Null, "the header string never became a TextBlock");
        var resting = ColorOf(label.Foreground);

        tab.IsSelected = true;
        Frame(tab);

        // Re-read rather than trust the reference taken above: a presenter is free to REBUILD its generated text, and
        // asserting on the old object would report a stale color as a defect.
        Assert.That(ColorOf(LabelOf(tab).Foreground), Is.Not.EqualTo(resting),
            "the selected tab's label kept the resting color - the plate says 'current' and the text does not");
    }

    [TestCase("Graphite")]
    [TestCase("Fluent")]
    public void DeselectingATab_PutsItsLabelColorBack(string themeName)
    {
        Use(Build(themeName));
        var tab = LiveTab();
        var label = LabelOf(tab);
        var resting = ColorOf(label.Foreground);

        tab.IsSelected = true;
        Frame(tab);
        tab.IsSelected = false;
        Frame(tab);

        Assert.That(ColorOf(LabelOf(tab).Foreground), Is.EqualTo(resting),
            "the label stayed in the selected color after the tab lost selection - it sticks on whichever tab was " +
            "current when the color was first written");
    }

    /// <summary>The seam itself, with no theme in sight: a presenter's own Foreground reaching the text it generated.
    /// Everything a theme does to a label - selected, hovered, disabled, accent - arrives through exactly this.</summary>
    [Test]
    public void AGeneratedLabelFollowsThePresentersForeground()
    {
        var red = Color.FromRgba(220, 40, 40, 255);
        var green = Color.FromRgba(40, 200, 90, 255);
        var presenter = new ContentPresenter { Content = "Shapes", Foreground = new SolidColorBrush(red) };
        presenter.Measure(new Size(200, 32));
        var label = presenter.VisualChildren.OfType<TextBlock>().FirstOrDefault();
        Assert.That(label, Is.Not.Null, "the string content never became a TextBlock");
        Assume.That(ColorOf(label.Foreground), Is.EqualTo(red), "precondition: the label starts in the presenter's color");

        presenter.Foreground = new SolidColorBrush(green);
        // A source change on an ELEMENT source is coalesced into the per-frame binding queue, so a test has to stand in
        // for the frame that would drain it.
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        presenter.Measure(new Size(200, 32));

        Assert.That(ColorOf(presenter.VisualChildren.OfType<TextBlock>().First().Foreground), Is.EqualTo(green),
            "the label kept the color the presenter held when the text was built");
    }

    /// <summary>The same seam for TEMPLATED content - the shape the stand actually uses, where a tab header comes from an
    /// ItemTemplate holding an AUTHORED TextBlock. The presenter deliberately never writes into one of those (an explicit
    /// write would outrank inheritance for good), so the color has to arrive by INHERITANCE, and it has to arrive again
    /// on every later change.</summary>
    [Test]
    public void ATemplatedLabelFollowsThePresentersForeground()
    {
        var red = Color.FromRgba(220, 40, 40, 255);
        var green = Color.FromRgba(40, 200, 90, 255);
        var presenter = new ContentPresenter
        {
            Content = "Shapes",
            Foreground = new SolidColorBrush(red),
            ContentTemplate = new Adamantium.UI.Core.Templates.DataTemplate(() =>
            {
                var text = new TextBlock { Text = "Shapes" };
                return new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = text };
            })
        };
        presenter.Measure(new Size(200, 32));
        var label = presenter.VisualChildren.OfType<TextBlock>().FirstOrDefault();
        Assert.That(label, Is.Not.Null, "the content template never produced a TextBlock");
        Assume.That(ColorOf(label.Foreground), Is.EqualTo(red), "precondition: the label starts in the presenter's color");

        presenter.Foreground = new SolidColorBrush(green);
        Adamantium.UI.Core.Data.BindingUpdateQueue.Flush();
        presenter.Measure(new Size(200, 32));

        Assert.That(ColorOf(presenter.VisualChildren.OfType<TextBlock>().First().Foreground), Is.EqualTo(green),
            "a templated label froze on the color the presenter held when the template was built");
    }

    /// <summary>The stand's actual shape: a tab whose header comes from a template, so the label is an AUTHORED
    /// TextBlock reached by inheritance - and the color is written by a TRIGGER rather than by hand. Each half of that
    /// works on its own; this is the pair.</summary>
    [TestCase("Graphite")]
    [TestCase("Fluent")]
    public void SelectingATab_ChangesATEMPLATEDLabelsColor(string themeName)
    {
        Use(Build(themeName));
        var tab = new TabItem
        {
            Header = "Shapes",
            HeaderTemplate = new Adamantium.UI.Core.Templates.DataTemplate(() =>
                new Adamantium.UI.Core.Templates.TemplateResult { RootComponent = new TextBlock { Text = "Shapes" } })
        };
        tab.ApplyCurrentTheme();
        Frame(tab);
        var resting = ColorOf(LabelOf(tab).Foreground);

        tab.IsSelected = true;
        Frame(tab);

        Assert.That(ColorOf(LabelOf(tab).Foreground), Is.Not.EqualTo(resting),
            "a templated tab label ignored the selection - the trigger wrote the presenter and the text never heard");
    }

    /// <summary>The report that found this: start in one theme, switch to the other, then click around the strip.
    /// A theme change rebuilds the template, so the trigger has to land on the NEW parts.</summary>
    [Test]
    public void AfterAThemeChange_TheLabelStillFollowsSelection()
    {
        Use(Build("Fluent"));
        var tab = LiveTab();
        tab.IsSelected = true;
        Frame(tab);

        Use(Build("Graphite"));
        tab.ApplyCurrentTheme();
        Frame(tab);

        var selected = ColorOf(LabelOf(tab).Foreground);

        tab.IsSelected = false;
        Frame(tab);

        Assert.That(ColorOf(LabelOf(tab).Foreground), Is.Not.EqualTo(selected),
            "after the theme changed, the label froze on the color it had at the moment of the change");
    }
}
