using System.IO;
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
/// theme, a scenario file in the language <c>adam-auto</c> takes, single commands, and a wait for a state rather than
/// for an element.
/// </summary>
[TestFixture]
public class RibbonShellScenarioTests
{
    private string _scenario;

    [SetUp]
    public void Fresh()
    {
        var app = HeadlessApplication.Start<Fluent>();
        app.ThemeManager.AddStyleSet<RibbonShellStyleSet>();
        app.ResourceManager.AddSource(new ModuleResources(), typeof(ModuleIcons), ResourceScope.Global);
        app.ResourceManager.AddSource(new ModuleResources(), typeof(RibbonShellIcons), ResourceScope.Global);
        _scenario = Path.Combine(Path.GetTempPath(), $"ribbon-{TestContext.CurrentContext.Test.ID}.adam");
    }

    [TearDown]
    public void Clean() => File.Delete(_scenario);

    private static async Task<AutomationSession> RibbonShell()
    {
        var shell = new RibbonShellView { DataContext = new RibbonShellViewModel() };
        var window = new Window { Width = 1280, Height = 720, ClientWidth = 1280, ClientHeight = 720, Content = shell };
        var session = AutomationSession.InProcess(window);
        await session.WaitForIdleAsync();
        return session;
    }

    [Test]
    public async Task AScenarioFile_RunsEveryStep_AndSaysWhatEachDid()
    {
        await File.WriteAllLinesAsync(_scenario,
        [
            "# the status line answers the command",
            "expect id=StatusLine name=Ready.",
            "invoke id=Cut",
            "wait id=StatusLine name=\"Cut the selection.\" --timeout 2s",
        ]);
        await using var session = await RibbonShell();
        var output = new StringWriter();

        await session.RunScenarioAsync(_scenario, output);

        Assert.That(output.ToString(), Does.Contain("> invoke id=Cut").And.Contain("every step passed"));
    }

    [Test]
    public async Task AScenarioThatFails_SaysWhichLineAndWhy()
    {
        await File.WriteAllLinesAsync(_scenario, ["invoke id=Cut", "expect id=StatusLine name=Ready."]);
        await using var session = await RibbonShell();

        var failure = Assert.ThrowsAsync<AutomationException>(() => session.RunScenarioAsync(_scenario));

        Assert.That(failure.Message, Does.Contain($"{_scenario}:2").And.Contain("expected \"Ready.\", was \"Cut the selection.\""));
    }

    [Test]
    public async Task ACommand_RunsAsOnALineOfAScenario()
    {
        await using var session = await RibbonShell();

        await session.RunCommandAsync("invoke id=Cut");

        Assert.That(await session.Find(By.Id("StatusLine")).NameAsync(), Is.EqualTo("Cut the selection."));
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
