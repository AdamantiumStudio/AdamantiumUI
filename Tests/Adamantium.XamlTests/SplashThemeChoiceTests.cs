using System;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.GraphiteTheme;
using Adamantium.UI.Themes.MacOsTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The splash window has the MacOs theme's close light when the application opens on that theme, which is told
/// before the built-in themes are made.</summary>
[TestFixture]
public class SplashThemeChoiceTests
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

    [TestCase("macOS", "Fluent", typeof(Fluent), true)]
    [TestCase("Fluent", "macOS", typeof(MacOs), false)]
    [TestCase("Own", "macOS", typeof(MacOs), false)]
    [TestCase(null, "macOS", typeof(Fluent), true)]
    [TestCase(null, "Own", typeof(MacOs), false)]
    [TestCase(null, "Graphite", typeof(MacOs), false)]
    [TestCase(null, "Forgotten", typeof(MacOs), true)]
    [TestCase(null, null, typeof(MacOs), true)]
    [TestCase(null, null, typeof(Graphite), false)]
    public void TheChoice_IsTheStarts(string requested, string named, Type startup, bool expected)
    {
        _themes.AddTheme("Own", new Theme("Own"));

        Assert.That(UIApplication.WillOpenOnMacOs(_themes, requested, named, startup), Is.EqualTo(expected));
    }

    [Test]
    public void WithNoThemeStated_ItIsThePlatforms() =>
        Assert.That(UIApplication.WillOpenOnMacOs(_themes, null, null, null), Is.EqualTo(OperatingSystem.IsMacOS()));

    [Test]
    public void TheBuiltInThemes_AreNamedAsTheStartExpects() => Assert.Multiple(() =>
    {
        Assert.That(new Fluent().Name, Is.EqualTo(UIApplication.FluentName));
        Assert.That(new Graphite().Name, Is.EqualTo(UIApplication.GraphiteName));
        Assert.That(new MacOs().Name, Is.EqualTo(UIApplication.MacOsName));
    });
}
