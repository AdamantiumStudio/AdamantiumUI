using System;
using System.Runtime.CompilerServices;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>A theme manager let go, with the theme it wore and what read that theme's resources, is collected: the OS
/// appearance and the theme's routing are static and must not keep them.</summary>
[TestFixture]
public class ThemeManagerLifetimeTests
{
    private FakeApp _app;

    [OneTimeSetUp]
    public void EnsureAppContext()
    {
        _app = new FakeApp(new AdamantiumDependencyContainer()) { ResourceManager = new ResourceManager() };
        UIAppContext.Initialize(_app, null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AManagerLetGo_AndTheThemeItWore_AreCollected(bool followingTheSystem)
    {
        var (manager, theme) = LetGo(followingTheSystem);
        RenderDirty.Clear();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.Multiple(() =>
        {
            Assert.That(manager.TryGetTarget(out _), Is.False, "the manager");
            Assert.That(theme.TryGetTarget(out _), Is.False, "the theme");
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference<ThemeManager> Manager, WeakReference<Theme> Theme) LetGo(bool followingTheSystem)
    {
        var current = typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current));
        var context = current.GetValue(null);
        var previous = (_app.ThemeManager, ((FakeContext)_app.UIContext).ThemeEngine, _app.ResourceManager);
        _app.ResourceManager = new ResourceManager();
        current.SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Adamantium.UI.Themes.GraphiteTheme.Graphite();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
        if (followingTheSystem)
        {
            Assert.That(themes.SetVariant(ThemeVariant.System), Is.True, "the theme follows the system");
        }

        var connected = new ThemeResource("AccentFillColorDefault").Apply(new Border(), "Background");
        Assert.That(connected, Is.Not.Null, "the border reads the theme");

        (_app.ThemeManager, ((FakeContext)_app.UIContext).ThemeEngine, _app.ResourceManager) = previous;
        current.SetValue(null, context);
        return (new WeakReference<ThemeManager>(themes), new WeakReference<Theme>(theme));
    }
}
