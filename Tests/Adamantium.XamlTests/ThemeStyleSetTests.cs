using System;
using System.Collections.Generic;
using System.Linq;
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

    private sealed class DuskStyleSet : StyleSet
    {
        protected override void OnInitialize(ITheme theme)
        {
            Add(new Style { Selector = new StyleSelector { Types = { typeof(Border) } } });
        }
    }

    private class Dusk() : Theme("dusk");

    private sealed class Midnight : Dusk;

    [Test]
    public void ASetPerTheme_GoesToTheThemesItNames_AndTheirsDerived_TheOtherSetToTheRest()
    {
        var manager = new ThemeManager(new AdamantiumDependencyContainer());
        var plain = new Theme("plain");
        var dusk = new Dusk();
        manager.AddTheme(plain.Name, plain);
        manager.AddTheme(dusk.Name, dusk);

        manager.AddStyleSet<LibraryStyleSet>(new Dictionary<Type, Type> { [typeof(Dusk)] = typeof(DuskStyleSet) });
        var midnight = new Midnight();
        manager.AddTheme("midnight", midnight);

        Assert.Multiple(() =>
        {
            Assert.That(plain.StyleSets.Select(set => set.GetType()), Is.EqualTo(new[] { typeof(LibraryStyleSet) }));
            Assert.That(dusk.StyleSets.Select(set => set.GetType()), Is.EqualTo(new[] { typeof(DuskStyleSet) }));
            Assert.That(midnight.StyleSets.Select(set => set.GetType()), Is.EqualTo(new[] { typeof(DuskStyleSet) }),
                "a theme added later, of a type deriving from the one named");
        });
    }

    [Test]
    public void TheSameSetAddedWithOtherSetsPerTheme_Throws_AndWithTheSameOnes_AddsNothing()
    {
        var manager = new ThemeManager(new AdamantiumDependencyContainer());
        var dusk = new Dusk();
        manager.AddTheme(dusk.Name, dusk);
        manager.AddStyleSet<LibraryStyleSet>(new Dictionary<Type, Type> { [typeof(Dusk)] = typeof(DuskStyleSet) });

        manager.AddStyleSet<LibraryStyleSet>(new Dictionary<Type, Type> { [typeof(Dusk)] = typeof(DuskStyleSet) });

        Assert.Multiple(() =>
        {
            Assert.That(dusk.StyleSets, Has.Count.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => manager.AddStyleSet<LibraryStyleSet>());
            Assert.Throws<ArgumentException>(() =>
                manager.AddStyleSet<DuskStyleSet>(new Dictionary<Type, Type> { [typeof(Dusk)] = typeof(Border) }));
        });
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
