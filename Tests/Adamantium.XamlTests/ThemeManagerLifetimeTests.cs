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

    [Test]
    public void AManagerLetGo_AndTheThemeItWore_AreCollected()
    {
        var (manager, theme) = LetGo();
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
    private (WeakReference<ThemeManager> Manager, WeakReference<Theme> Theme) LetGo()
    {
        var previous = (_app.ThemeManager, ((FakeContext)_app.UIContext).ThemeEngine, _app.ResourceManager);
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        var theme = new Adamantium.UI.Themes.GraphiteTheme.Graphite();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
        var connected = new ThemeResource("AccentFillColorDefault").Apply(new Border(), "Background");
        Assert.That(connected, Is.Not.Null, "the border reads the theme");

        (_app.ThemeManager, ((FakeContext)_app.UIContext).ThemeEngine, _app.ResourceManager) = previous;
        return (new WeakReference<ThemeManager>(themes), new WeakReference<Theme>(theme));
    }
}
