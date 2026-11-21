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
        await using var session = await LaunchAsync();

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

        // The mouse comes over the page beside the card, onto the card, over its fields and buttons: the dialog stays,
        // still asking.
        if (OperatingSystem.IsWindows())
        {
            var page = (await session.Find(By.Id("WebPage")).WaitForAsync()).Bounds;
            var card = (await session.Find(By.Id("WebSignIn")).WaitForAsync()).Bounds;
            var accept = (await session.Find(By.Id("WebSignInAccept")).WaitForAsync()).Bounds;
            var besideX = page[0] + 30;
            var middleY = card[1] + card[3] / 2;
            await RealMouse.MoveAsync(session, besideX, page[1] + 30, besideX, middleY);
            await RealMouse.MoveAsync(session, besideX, middleY, card[0] + card[2] / 2, middleY);
            await RealMouse.MoveAsync(session, card[0] + card[2] / 2, middleY, accept[0] + accept[2] / 2, accept[1] + accept[3] / 2);
            await Task.Delay(700);
            await session.WaitForIdleAsync();
            await session.Find(By.Id("WebSignIn")).ExpectAsync(Shown);
        }

        // A wrong password: asked again, saying why, with the name kept.
        await session.Find(By.Id("WebSignInUser")).TypeAsync("anna");
        await session.Find(By.Id("WebSignInPassword")).TypeAsync("wrong");
        await session.Find(By.Id("WebSignInAccept")).InvokeAsync();
        await session.Find(By.Id("WebSignInError")).WaitUntilAsync([Shown], Asked);
        await session.Find(By.Id("WebSignInUser")).ExpectAsync(new Expectation("value", "anna"));
        await session.Find(By.Id("WebSignInPassword")).WaitUntilAsync([new Expectation("focus", "True")]);

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

    // An address typed over a page still loading: the site asks, and the page gone on from ending its load a moment later
    // takes nothing of the question - the dialog stays until it is answered.
    [Test]
    public async Task ASiteAskingToSignIn_OverAPageStillLoading_StaysAsked()
    {
        using var site = new SigningInSite("anna", "secret");
        await using var session = await LaunchAsync();

        await session.Find(By.Id("GalleryTabs")).Find(By.Id("Web")).SelectAsync();
        await session.Find(By.Id("WebPage")).WaitForAsync();
        await session.Find(By.Id("WebAddress")).SetValueAsync(site.LoadingPage);
        await session.Find(By.Id("WebAddress")).PressKeysAsync("Enter");
        // In - its document read, its script asked for - and loading that script for some three seconds.
        await Until(() => site.IsLoadingPageIn, CefStart, "the loading page is in");

        await session.Find(By.Id("WebAddress")).SetValueAsync(site.Origin + "/");
        await session.Find(By.Id("WebAddress")).PressKeysAsync("Enter");
        await session.Find(By.Id("WebSignIn")).WaitUntilAsync([Shown], Asked);
        Assert.That(site.IsLoadingPageSent, Is.False, "asked while the page gone on from still loads");
        // The page gone on from ends its load: its script sent whole, and a moment for Chromium to say so.
        await Until(() => site.IsLoadingPageSent, Asked, "the loading page's script is sent");
        await Task.Delay(TimeSpan.FromSeconds(1));
        await session.WaitForIdleAsync();

        await session.Find(By.Id("WebSignIn")).ExpectAsync(Shown);
    }

    // On a desktop of its own, off the user's screen, as the generated scenarios run; the mouse still comes as Windows'
    // own messages, posted to the window there.
    private static Task<AutomationSession> LaunchAsync() => AutomationSession.LaunchAsync(
        Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Adamantium.UI.Sandbox.exe" : "Adamantium.UI.Sandbox"),
        new LaunchOptions
        {
            FreshData = true,
            VirtualDisplay = true,
            Environment = { ["ADAM_UI_FONT"] = "Content/SandboxContent/Fonts/RobotoFlex-Variable.ttf" }
        });

    private static async Task Until(Func<bool> done, TimeSpan timeout, string what)
    {
        var until = DateTime.UtcNow + timeout;
        while (!done())
        {
            if (DateTime.UtcNow > until)
            {
                Assert.Fail($"Not within {timeout.TotalSeconds} s: {what}.");
            }

            await Task.Delay(50);
        }
    }
}
