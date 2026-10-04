using System.Threading.Tasks;
using Adamantium.Core.DependencyInjection;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.ViewModels;
using Adamantium.UI.Sandbox.Views;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.XamlTests;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>
/// The sandbox's ribbon shell driven headless, the way an agent drives the running sandbox: a command found by its id,
/// pressed by what the button can do or clicked by input simulated inside the framework, and the status line read back.
/// </summary>
[TestFixture]
public class RibbonShellAutomationTests
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
        _app.ResourceManager = new ResourceManager();
        typeof(UIAppContext).GetProperty(nameof(UIAppContext.Current)).SetValue(null, _app);
        var themes = new ThemeManager(new AdamantiumDependencyContainer());
        _app.ThemeManager = themes;
        ((FakeContext)_app.UIContext).ThemeEngine = themes;
        themes.AddStyleSet<RibbonShellStyleSet>();
        _app.ResourceManager.AddSource(new ModuleResources(), typeof(ModuleIcons), ResourceScope.Global);

        var theme = new Fluent();
        themes.AddTheme(theme.Name, theme);
        themes.SetTheme(theme);
    }

    private static async Task<AutomationSession> RibbonShell()
    {
        var shell = new RibbonShellView { DataContext = new RibbonShellViewModel() };
        var window = new Window { Width = 1280, Height = 720, ClientWidth = 1280, ClientHeight = 720, Content = shell };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task Cut_ByItsId_SaysSoInTheStatusLine()
    {
        await using var session = await RibbonShell();
        var status = session.Find(By.Id("StatusLine"));
        var before = await status.NameAsync();

        await session.Find(By.Id("Cut")).InvokeAsync();
        var after = await status.NameAsync();

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo("Ready."));
            Assert.That(after, Is.EqualTo("Cut the selection."));
        });
    }

    [Test]
    public async Task Cut_ClickedThroughTheFrameworksInput_SaysSoToo()
    {
        await using var session = await RibbonShell();

        await session.Find(By.Id("Cut")).ClickAsync();

        Assert.That(await session.Find(By.Id("StatusLine")).NameAsync(), Is.EqualTo("Cut the selection."));
    }
}
