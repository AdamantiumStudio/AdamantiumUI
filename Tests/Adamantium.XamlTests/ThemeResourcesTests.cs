using System;
using System.Collections.Generic;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

// A library gives its controls resources - icons above all - through IThemeManager.AddResources: they are in force
// whichever theme is current, the ones made current later included, and a theme declaring the same keys has its own way.
[TestFixture]
public class ThemeResourcesTests
{
    private const string Key = "LibraryIcon";

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

    private sealed class LibraryIcons : ResourceDictionary
    {
        public LibraryIcons()
        {
            Add(Key, "library");
        }
    }

    private sealed class DuskIcons : ResourceDictionary
    {
        public DuskIcons()
        {
            Add(Key, "dusk");
        }
    }

    private class Dusk() : Theme("dusk");

    private static Theme ThemeWithItsOwnIcon(string name)
    {
        var theme = new Theme(name);
        ResourceContext.SetScope(theme, ResourceScope.Theme);
        var own = new ResourceDictionary();
        own.Add(Key, "theme");
        ResourceContext.SetResources(theme, own);
        return theme;
    }

    private object Found() => _app.ResourceManager.FindResourceInScope(Key, ResourceScope.Theme);

    private ThemeManager Manager(params Theme[] themes)
    {
        var manager = new ThemeManager(new AdamantiumDependencyContainer());
        foreach (var theme in themes)
        {
            manager.AddTheme(theme.Name, theme);
        }

        return manager;
    }

    [Test]
    public void AddedBeforeAThemeIsCurrent_IsInForceOnceItIs()
    {
        var theme = new Theme("plain");
        var manager = Manager(theme);

        manager.AddResources<LibraryIcons>();
        Assert.That(Found(), Is.Null, "no theme is current yet");

        manager.SetTheme(theme);
        Assert.That(Found(), Is.EqualTo("library"));
    }

    [Test]
    public void AddedWhileAThemeIsCurrent_IsInForceAtOnce()
    {
        var theme = new Theme("plain");
        var manager = Manager(theme);
        manager.SetTheme(theme);

        manager.AddResources<LibraryIcons>();

        Assert.That(Found(), Is.EqualTo("library"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AThemeDeclaringTheSameKey_HasItsOwnWay_WhenEverTheLibraryCame(bool addedFirst)
    {
        var theme = ThemeWithItsOwnIcon("own");
        var manager = Manager(theme);
        if (addedFirst) manager.AddResources<LibraryIcons>();
        manager.SetTheme(theme);
        if (!addedFirst) manager.AddResources<LibraryIcons>();

        Assert.That(Found(), Is.EqualTo("theme"));
    }

    // Put under the theme in force rather than the theme taken down and made again: a value set live stays.
    [Test]
    public void AddedWhileAThemeIsCurrent_KeepsAValueSetLive()
    {
        var theme = new Theme("plain");
        ResourceContext.SetScope(theme, ResourceScope.Theme);
        var own = new ResourceDictionary();
        own.Add("Other", "as declared");
        ResourceContext.SetResources(theme, own);
        var manager = Manager(theme);
        manager.SetTheme(theme);
        _app.ResourceManager.SetResourceInScope("Other", "set live", ResourceScope.Theme);

        manager.AddResources<LibraryIcons>();

        Assert.Multiple(() =>
        {
            Assert.That(_app.ResourceManager.FindResourceInScope("Other", ResourceScope.Theme), Is.EqualTo("set live"));
            Assert.That(Found(), Is.EqualTo("library"));
        });
    }

    // Each theme made current by a manager of its own: a second SetTheme swaps on a later frame, after the busy overlay
    // is drawn, and these tests draw no frames.
    [TestCase(false, "library")]
    [TestCase(true, "dusk")]
    public void ADictionaryPerTheme_GoesToTheThemesItNames_TheOtherToTheRest(bool dusky, string expected)
    {
        Theme theme = dusky ? new Dusk() : new Theme("plain");
        var manager = Manager(theme);
        manager.AddResources<LibraryIcons>(new Dictionary<Type, Type> { [typeof(Dusk)] = typeof(DuskIcons) });

        manager.SetTheme(theme);

        Assert.That(Found(), Is.EqualTo(expected));
    }

    [Test]
    public void AddedTwice_IsAddedOnce_WithOtherDictionariesPerTheme_Throws_AndNotADictionary_Throws()
    {
        var theme = new Theme("plain");
        var manager = Manager(theme);
        manager.SetTheme(theme);

        manager.AddResources<LibraryIcons>();
        manager.AddResources<LibraryIcons>();

        Assert.Multiple(() =>
        {
            Assert.That(Found(), Is.EqualTo("library"));
            Assert.Throws<InvalidOperationException>(() =>
                manager.AddResources<LibraryIcons>(new Dictionary<Type, Type> { [typeof(Dusk)] = typeof(DuskIcons) }));
            Assert.Throws<ArgumentException>(() => manager.AddResources(typeof(Border)));
        });
    }
}
