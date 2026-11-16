using System;
using System.IO;
using System.Threading.Tasks;
using Adamantium.UI.Automation;
using NUnit.Framework;

namespace Adamantium.UI.Sandbox.Tests;

/// <summary>
/// The Web page signing in to a site that asks for it, driven as a user would: the dialog over the page says the site
/// and that the connection is not private; a wrong password is asked again with why, the name kept; Esc cancels;
/// reloaded, the right password on Enter lets the page in. Written by hand, not as a scenario file: the site is this
/// test's own, on this machine.
/// </summary>
[TestFixture]
[Category("Scenario")]
[NonParallelizable]
public class WebSignInScenarioTests
{
    private static readonly TimeSpan CefStart = TimeSpan.FromSeconds(40);
    private static readonly TimeSpan Asked = TimeSpan.FromSeconds(15);

    // A collapsed part stays in the tree, off screen.
    private static readonly Expectation Shown = new("offscreen", "False");
    private static readonly Expectation Hidden = new("offscreen", "True");

    [Test]
    public async Task ASiteAskingToSignIn_IsAnsweredInTheDialogOverThePage()
    {
        using var site = new SigningInSite("anna", "secret");
        await using var session = await AutomationSession.LaunchAsync(
            Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Adamantium.UI.Sandbox.exe" : "Adamantium.UI.Sandbox"),
            new LaunchOptions
            {
                FreshData = true,
                VirtualDisplay = true,
                Environment = { ["ADAM_UI_FONT"] = "Content/SandboxContent/Fonts/RobotoFlex-Variable.ttf" }
            });

        await session.Find(By.Id("GalleryTabs")).Find(By.Id("Web")).SelectAsync();
        await session.Find(By.Id("WebPage")).WaitForAsync();
        await session.Find(By.Id("WebAddress")).SetValueAsync(site.Origin + "/");
        await session.Find(By.Id("WebAddress")).PressKeysAsync("Enter");

        // Asked: the site and the warning shown, no error yet, nothing to remember with no store.
        await session.Find(By.Id("WebSignIn")).WaitUntilAsync([Shown], CefStart);
        await session.Find(By.Id("WebSignInInsecure")).ExpectAsync(Shown);
        await session.Find(By.Id("WebSignInError")).ExpectAsync(Hidden);
        await session.Find(By.Id("WebSignInRemember")).ExpectAsync(Hidden);
        await session.Find(By.Id("WebSignInUser")).WaitUntilAsync([new Expectation("focus", "True")]);

        // A wrong password: asked again, saying why, with the name kept.
        await session.Find(By.Id("WebSignInUser")).TypeAsync("anna");
        await session.Find(By.Id("WebSignInPassword")).TypeAsync("wrong");
        await session.Find(By.Id("WebSignInAccept")).InvokeAsync();
        await session.Find(By.Id("WebSignInError")).WaitUntilAsync([Shown], Asked);
        await session.Find(By.Id("WebSignInUser")).ExpectAsync(new Expectation("value", "anna"));

        // Esc cancels: the dialog goes, the page keeps the site's refusal.
        await session.Find(By.Id("WebSignInPassword")).PressKeysAsync("Escape");
        await session.Find(By.Id("WebSignIn")).WaitUntilAsync([Hidden], Asked);
        Assert.That(site.Admitted, Is.Zero, "nobody let in yet");

        // Reloaded, it asks anew - no error - and the right password on Enter lets the page in.
        await session.Find(By.Id("WebReload")).InvokeAsync();
        await session.Find(By.Id("WebSignIn")).WaitUntilAsync([Shown], Asked);
        await session.Find(By.Id("WebSignInError")).ExpectAsync(Hidden);
        await session.Find(By.Id("WebSignInUser")).ExpectAsync(new Expectation("value", ""));
        await session.Find(By.Id("WebSignInUser")).TypeAsync("anna");
        await session.Find(By.Id("WebSignInPassword")).TypeAsync("secret");
        await session.Find(By.Id("WebSignInPassword")).PressKeysAsync("Enter");
        await session.Find(By.Id("WebSignIn")).WaitUntilAsync([Hidden], Asked);
        await session.WaitForIdleAsync();
        Assert.That(site.Admitted, Is.GreaterThan(0), "let in with the right password");
    }
}
