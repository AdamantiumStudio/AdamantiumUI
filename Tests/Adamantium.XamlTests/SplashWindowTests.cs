using System.Linq;
using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Base;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.Themes.GraphiteTheme;
using Adamantium.UI.Themes.SplashTheme;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>The splash window wears a theme of its own, which the application's theme - made and set while the splash
/// window is already drawn - never reaches; its close button closes it.</summary>
[TestFixture]
public class SplashWindowTests
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
        _app.Windows = [];
        Adamantium.UI.Core.Media.Animation.AnimationManager.Reset();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        _themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = _themes;
        ((FakeContext)_app.UIContext).ThemeEngine = _themes;
    }

    private static SplashWindow Splash()
    {
        var splash = new Unhosted { Width = 400, Height = 240, ClientWidth = 400, ClientHeight = 240, Content = new Grid() };
        ThemeContext.SetTheme(splash, new Splash());
        return splash;
    }

    private sealed class Unhosted : SplashWindow
    {
        protected override IWindowWorkerService CreateWindowWorker(IUIContext context) => null;
    }

    [Test]
    public async Task TheApplicationsFirstTheme_LeavesTheSplashWindowAsItIs()
    {
        var splash = Splash();
        splash.AttachContextAndInitialize(_app.UIContext);
        _app.Windows = [splash];
        var session = AutomationSession.InProcess(splash);
        await session.WaitForIdleAsync();
        var close = splash.GetTemplateChild("PART_CloseButton");
        var changing = false;
        splash.PropertyChanged += (_, e) => changing |= e.Property == WindowBase.IsThemeChangingProperty;

        var fluent = new Fluent();
        _themes.AddTheme(fluent.Name, fluent);
        _themes.SetTheme(fluent);
        var untouched = splash.IsStyleApplied && ((FundamentalUIComponent)close).IsStyleApplied;
        await session.WaitForIdleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(untouched, Is.True, "not marked for restyling by a theme it does not wear");
            Assert.That(close, Is.Not.Null, "the splash theme's template");
            Assert.That(splash.GetTemplateChild("PART_CloseButton"), Is.SameAs(close), "not restyled by the application's theme");
            Assert.That(changing, Is.False, "and no busy state for a swap that is not its own");
        });
    }

    [Test]
    public async Task AThemeSwap_LeavesTheSplashWindowAsItIs()
    {
        var fluent = new Fluent();
        _themes.AddTheme(fluent.Name, fluent);
        _themes.SetTheme(fluent);
        var splash = Splash();
        _app.Windows = [splash];
        var session = AutomationSession.InProcess(splash);
        await session.WaitForIdleAsync();
        var close = splash.GetTemplateChild("PART_CloseButton");

        var graphite = new Graphite();
        _themes.AddTheme(graphite.Name, graphite);
        _themes.SetTheme(graphite);
        await session.WaitForIdleAsync();

        Assert.That(splash.GetTemplateChild("PART_CloseButton"), Is.SameAs(close));
    }

    [Test]
    public async Task TheCloseButton_ClosesTheWindow()
    {
        var splash = Splash();
        var closed = false;
        splash.Closed += (_, _) => closed = true;
        var session = AutomationSession.InProcess(splash);
        await session.WaitForIdleAsync();
        // The session keeps looking at the window it was made for, which is closed and released by then.
        session.AllowErrors = true;

        await session.Find(By.Id("Close")).InvokeAsync();

        Assert.That(closed, Is.True);
    }

    // Read through IWindow, as the hand-over reads it: a window that has drawn but was not shown is not up.
    [Test]
    public void AWindowNotShown_IsNotUp()
    {
        IWindow window = new Window();

        Assert.That(window.IsDisplayed, Is.False);
    }

    [Test]
    public async Task WithoutTheCloseButton_ThereIsNone()
    {
        var splash = Splash();
        splash.ShowCloseButton = false;
        var session = AutomationSession.InProcess(splash);
        await session.WaitForIdleAsync();

        Assert.That(((UIComponent)splash.GetTemplateChild("PART_CloseButton")).Visibility, Is.EqualTo(Visibility.Collapsed));
    }
}
