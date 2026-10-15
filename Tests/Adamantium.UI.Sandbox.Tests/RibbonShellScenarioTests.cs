using System.Threading.Tasks;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Core.Resources;
using Adamantium.UI.Sandbox.ModuleLoading;
using Adamantium.UI.Sandbox.Resources;
using Adamantium.UI.Sandbox.ViewModels;
using Adamantium.UI.Sandbox.Views;
using Adamantium.UI.Themes.FluentTheme;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>
/// What a user of the framework writes to test an application of their own: a headless application on the application's
/// theme, what its elements are expected to read, and a wait for a state rather than for an element.
/// </summary>
[TestFixture]
public class RibbonShellScenarioTests
{
    [SetUp]
    public void Fresh()
    {
        var app = HeadlessApplication.Start<Fluent>();
        app.ThemeManager.AddStyleSet<RibbonShellStyleSet>();
        app.ResourceManager.AddSource(new ModuleResources(), typeof(ModuleIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(RibbonShellIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(CaptionIcons), ResourceScope.Global);
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
    public async Task TheStatusLine_ReadsAsExpected_AsACommandChangesIt()
    {
        await using var session = await RibbonShell();
        var status = session.Find("id=StatusLine");

        await status.ExpectAsync(new Expectation("name", "Ready."));
        await session.Find("id=Cut").InvokeAsync();
        var reached = await status.WaitUntilAsync([new Expectation("name", "Cut the selection.")], System.TimeSpan.FromSeconds(2));

        Assert.That(reached.Element.Name, Is.EqualTo("Cut the selection."));
    }

    [Test]
    public async Task AMismatch_SaysWhatTheElementReadInstead()
    {
        await using var session = await RibbonShell();
        await session.Find("id=Cut").InvokeAsync();

        var failure = Assert.ThrowsAsync<AutomationException>(() => session.Find("id=StatusLine").ExpectAsync(new Expectation("name", "Ready.")));

        Assert.That(failure.Message, Does.Contain("expected \"Ready.\", was \"Cut the selection.\""));
    }

    [Test]
    public async Task WaitingUntil_ReturnsOnceTheStateIsReached_AndSaysHowItWasLastSeenIfNot()
    {
        await using var session = await RibbonShell();
        var status = session.Find(By.Id("StatusLine"));
        await session.Find(By.Id("Cut")).InvokeAsync();

        var reached = await status.WaitUntilAsync(info => info.Name == "Cut the selection.");
        var never = Assert.ThrowsAsync<AutomationException>(() =>
            status.WaitUntilAsync(info => info.Name == "Never.", System.TimeSpan.FromMilliseconds(200)));

        Assert.That(reached.Name, Is.EqualTo("Cut the selection."));
        Assert.That(never.Message, Does.Contain("Cut the selection."));
    }
}
