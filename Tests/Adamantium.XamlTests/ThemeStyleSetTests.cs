using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// A library gives its controls their look through IThemeManager.AddStyleSet: every theme, the ones added later and the
// variants already made included, has to carry the set, each initialized with itself.
[TestFixture]
public class ThemeStyleSetTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
    }

    [SetUp]
    public void FreshResources()
    {
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
    }

    // Has no styles until it is initialized, as a set generated from markup.
    private sealed class LibraryStyleSet : StyleSet
    {
        protected override void OnInitialize(ITheme theme)
        {
            Add(new Style { Selector = new StyleSelector { Types = { typeof(Border) } } });
        }
    }

    private static Style[] StylesOf(ITheme theme) => theme.FindStylesForComponent(new Border());

    [Test]
    public void ASetAddedToATheme_IsInitializedWithIt()
    {
        var theme = new Theme("a");

        theme.AddStyleSet(new LibraryStyleSet());

        Assert.That(StylesOf(theme), Has.Length.EqualTo(1), "a set made from markup has no styles until it is initialized");
    }

    [Test]
    public void TheManagersSet_ReachesEveryTheme_EachWithItsOwnInstance()
    {
        var manager = new ThemeManager(new AdamantiumDependencyContainer());
        var first = new Theme("first");
        var second = new Theme("second");
        manager.AddTheme(first.Name, first);
        manager.AddTheme(second.Name, second);

        manager.AddStyleSet<LibraryStyleSet>();

        Assert.That(StylesOf(first), Has.Length.EqualTo(1));
        Assert.That(StylesOf(second), Has.Length.EqualTo(1));
        Assert.That(StylesOf(first)[0], Is.Not.SameAs(StylesOf(second)[0]), "each theme initializes an instance of its own");
    }

    [Test]
    public void AThemeAddedLater_GetsTheSetToo()
    {
        var manager = new ThemeManager(new AdamantiumDependencyContainer());
        manager.AddStyleSet<LibraryStyleSet>();

        var later = new Theme("later");
        manager.AddTheme(later.Name, later);

        Assert.That(StylesOf(later), Has.Length.EqualTo(1));
    }

    [Test]
    public void AddingTheSetTwice_AddsItOnce()
    {
        var manager = new ThemeManager(new AdamantiumDependencyContainer());
        var theme = new Theme("once");
        manager.AddTheme(theme.Name, theme);

        manager.AddStyleSet<LibraryStyleSet>();
        manager.AddStyleSet<LibraryStyleSet>();

        Assert.That(StylesOf(theme), Has.Length.EqualTo(1));
    }

    [Test]
    public void AVariantMadeBeforeTheSet_GetsItToo()
    {
        var theme = new Theme("variants");
        theme.AddVariant(new ThemeVariantDefinition(ThemeVariant.Light));
        theme.AddVariant(new ThemeVariantDefinition(ThemeVariant.Dark));
        theme.ApplyVariant(ThemeVariant.Light);
        var dark = theme.SiblingForVariant(ThemeVariant.Dark);

        theme.AddStyleSet(new LibraryStyleSet());

        Assert.That(StylesOf(dark), Has.Length.EqualTo(1), "a variant shares every style with the theme it was made from");
    }
}
