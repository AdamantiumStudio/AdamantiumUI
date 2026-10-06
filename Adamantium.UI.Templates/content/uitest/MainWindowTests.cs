using Adamantium.UI.Automation;
using Adamantium.UI.Themes.THEME_NAMETheme;
using APP_NAME;
using NUnit.Framework;

namespace AdamantiumApp.UITests;

/// <summary>
/// The main window driven two ways. Headless: built in this process on the application's theme, no window of the system
/// and no GPU - quick, and where most tests belong. Launched: the application itself, started with its automation agent
/// - it has to opt in by calling <c>this.UseAutomationAgent()</c> in its constructor, which does nothing unless a test
/// or adam-auto starts it.
/// </summary>
[TestFixture]
public class MainWindowTests
{
    private static string Scenario(string name) => Path.Combine(AppContext.BaseDirectory, "Scenarios", name);

    private static async Task<AutomationSession> HeadlessAsync()
    {
        HeadlessApplication.Start<THEME_NAME>();
        var session = AutomationSession.InProcess(new MainWindow());
        await session.WaitForIdleAsync();
        return session;
    }

    private static Task<AutomationSession> LaunchedAsync() =>
        AutomationSession.LaunchAsync(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "APP_NAME.exe" : "APP_NAME"));

    [Test]
    public async Task Headless_TheWindowIsThere()
    {
        await using var session = await HeadlessAsync();

        var window = await session.Find(By.Id("MainWindow")).GetAsync();

        Assert.That(window.Name, Is.EqualTo("APP_NAME"));
    }

    [Test]
    public async Task Headless_TheScenarioPasses()
    {
        await using var session = await HeadlessAsync();

        await session.RunScenarioAsync(Scenario("MainWindow.adam"), TestContext.Out);
    }

    [Test]
    public async Task Launched_TheWindowIsThere()
    {
        await using var session = await LaunchedAsync();

        var window = await session.Find(By.Id("MainWindow")).WaitUntilAsync(info => !info.IsOffscreen);

        Assert.That(window.Name, Is.EqualTo("APP_NAME"));
    }

    [Test]
    public async Task Launched_TheScenarioPasses()
    {
        await using var session = await LaunchedAsync();

        await session.RunScenarioAsync(Scenario("MainWindow.adam"), TestContext.Out);
    }
}
