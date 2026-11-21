using System;
using System.Linq;
using Adamantium.UI.WebView.Cef;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

/// <summary>
/// A site or a proxy asking to sign in is asked of the view, with who asks; the right name and password let the page
/// in, a wrong one is asked again, a refusal leaves the server's answer - and a page that goes leaves nothing waiting.
/// </summary>
public partial class WebViewFrameTests
{
    private const string Inside = "<title>inside</title><body></body>";
    private static readonly WebCredential Anna = new("anna", "secret");

    [Test]
    public void ASiteAskingToSignIn_IsAskedOfTheView_AndTheRightAnswerLetsThePageIn()
    {
        using var site = LocalSite.SigningIn(Inside, Anna, "Lab");
        WebSignInTarget target = null;
        string address = null, title = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>start</title>", (browser, view) =>
        {
            browser.Navigate(site.Origin + "/");
            Pump(() => view.SignIns.Count > 0, TimeSpan.FromSeconds(10));
            var asked = view.SignIns.FirstOrDefault();
            target = asked?.Target;
            address = asked?.Address;
            asked?.Answer(Anna);
            Pump(() => view.Title == "inside", TimeSpan.FromSeconds(10));
            title = view.Title;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(target, Is.EqualTo(new WebSignInTarget("127.0.0.1", site.Port, "Lab", "basic", false)));
            Assert.That(address, Does.StartWith(site.Origin));
            Assert.That(title, Is.EqualTo("inside"));
        });
    }

    // The owner's case, 16.11: a site out there on http, answered after the user took a while. Chrome's upgrade of the
    // address to https gave its attempt three seconds, then fell back to http without saying, and the answer went to the
    // request given up - the page loaded on for ever. Network: a site out there is the only one Chrome would upgrade.
    [Test]
    [Explicit("needs the network")]
    public void ASiteOutThereOnHttp_AnsweredAfterAWhile_LetsThePageIn()
    {
        WebViewSignInRequest asked = null;
        string error = null;
        var completed = false;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>start</title>", (browser, view) =>
        {
            browser.Navigate("http://httpbin.org/basic-auth/anna/secret");
            Pump(() => view.SignIns.Count > 0, TimeSpan.FromSeconds(15));
            asked = view.SignIns.FirstOrDefault();
            Pump(() => view.Error != null, TimeSpan.FromSeconds(5));
            asked?.Answer(Anna);
            completed = Pump(() => view.Completed.Any(address => address.Contains("httpbin.org")), TimeSpan.FromSeconds(15));
            error = view.Error?.Text;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked?.Target.Scheme, Is.EqualTo("basic"));
            Assert.That(error, Is.Null, "no load failed while the user typed");
            Assert.That(completed, Is.True, "the page let in");
        });
    }

    [Test]
    public void AWrongSignIn_IsAskedAgain_AndARefusalLeavesThePageOut()
    {
        using var site = LocalSite.SigningIn(Inside, Anna, "Lab");
        int asked = 0, loaded = 0;
        string title = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>start</title>", (browser, view) =>
        {
            browser.Navigate(site.Origin + "/");
            Pump(() => view.SignIns.Count > 0, TimeSpan.FromSeconds(10));
            view.SignIns.FirstOrDefault()?.Answer(new WebCredential("anna", "wrong"));
            Pump(() => view.SignIns.Count > 1, TimeSpan.FromSeconds(10));
            asked = view.SignIns.Count;
            view.SignIns.LastOrDefault()?.Refuse();
            Pump(() => view.Completed.Count > 0, TimeSpan.FromSeconds(10));
            loaded = view.Completed.Count;
            title = view.Title;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(2), "asked again after the wrong password");
            Assert.That(loaded, Is.GreaterThan(0), "the refusal loaded as the server answered");
            Assert.That(title, Is.Not.EqualTo("inside"));
            Assert.That(site.Refusals, Is.GreaterThanOrEqualTo(2));
        });
    }

    [Test]
    public void AProxyAskingToSignIn_IsAskedAsAProxy_AndLetsThePageThrough()
    {
        using var proxy = LocalSite.ProxySigningIn("<title>proxied</title><body></body>", Anna, "Gate");
        WebSignInTarget target = null;
        string title = null;
        try
        {
            loop.Run(() =>
            {
                // Loopback goes straight by Chromium's default; the test's proxy is on loopback too.
                engine.Proxy = new WebViewProxy
                {
                    Mode = WebViewProxyMode.Server,
                    Kind = WebViewProxyKind.Http,
                    Server = "127.0.0.1",
                    Port = proxy.Port,
                    Bypass = ["<-loopback>"]
                };
                ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>start</title>", (browser, view) =>
                {
                    browser.Navigate("http://site.invalid/");
                    Pump(() => view.SignIns.Count > 0, TimeSpan.FromSeconds(10));
                    var asked = view.SignIns.FirstOrDefault();
                    target = asked?.Target;
                    asked?.Answer(Anna);
                    Pump(() => view.Title == "proxied", TimeSpan.FromSeconds(10));
                    title = view.Title;
                });
            });
        }
        finally
        {
            loop.Run(() => engine.Proxy = WebViewProxy.System);
        }

        Assert.Multiple(() =>
        {
            Assert.That(target, Is.EqualTo(new WebSignInTarget("127.0.0.1", proxy.Port, "Gate", "basic", true)));
            Assert.That(title, Is.EqualTo("proxied"));
        });
    }

    // Chromium keeps the proxy in the profile: the system's way set after a server clears it, and pages go straight.
    [Test]
    public void TheSystemsWay_ClearsAServerSetBefore()
    {
        using var site = new LocalSite("<title>straight</title><body></body>");
        using var closed = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        closed.Start();
        var deadPort = ((System.Net.IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        string title = null;
        try
        {
            loop.Run(() =>
            {
                engine.Proxy = new WebViewProxy
                {
                    Mode = WebViewProxyMode.Server,
                    Server = "127.0.0.1",
                    Port = deadPort,
                    Bypass = ["<-loopback>"]
                };
                engine.Proxy = WebViewProxy.System;
                ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>start</title>", (browser, view) =>
                {
                    browser.Navigate(site.Origin + "/");
                    Pump(() => view.Title == "straight", TimeSpan.FromSeconds(10));
                    title = view.Title;
                });
            });
        }
        finally
        {
            loop.Run(() => engine.Proxy = WebViewProxy.System);
        }

        Assert.That(title, Is.EqualTo("straight"), "gone straight, not to the server set before");
    }

    [Test]
    public void AServerWithNoHost_IsRefused() =>
        Assert.Throws<ArgumentException>(() => engine.Proxy = new WebViewProxy { Mode = WebViewProxyMode.Server });

    // Null given for a text or the list is taken as none: the engine can always pass them on.
    [Test]
    public void AProxyGivenNulls_TakesThemAsNone()
    {
        var proxy = new WebViewProxy { Server = null, Bypass = null, ScriptAddress = null };

        Assert.Multiple(() =>
        {
            Assert.That(proxy.Server, Is.Empty);
            Assert.That(proxy.Bypass, Is.Empty);
            Assert.That(proxy.ScriptAddress, Is.Empty);
        });
    }

    [Test]
    public void ASignInStillAsked_IsRefused_WhenThePageGoes()
    {
        using var site = LocalSite.SigningIn(Inside, Anna, "Lab");
        WebViewSignInRequest asked = null;
        var answeredBefore = true;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>start</title>", (browser, view) =>
        {
            browser.Navigate(site.Origin + "/");
            Pump(() => view.SignIns.Count > 0, TimeSpan.FromSeconds(10));
            asked = view.SignIns.FirstOrDefault();
            answeredBefore = asked?.IsAnswered ?? true;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Not.Null, "the view was asked");
            Assert.That(answeredBefore, Is.False, "it waited for the view");
            Assert.That(asked?.IsAnswered, Is.True, "refused as the page went");
        });
    }

    // What WebView's sign-in dialog stands on: a page gone on from while it still loads - a start page out on the network,
    // the address typed over it - ends AFTER the page gone to asks to sign in, the new one not yet in. That end is not
    // the asking page's (IsOfThePageShown), and takes nothing of its question (WebView.SignInsLoaded); answered, the page
    // that asked is the one shown.
    [Test]
    public void APageStillLoading_EndsAfterTheNextOneAsksToSignIn_WhoseQuestionIsNotOfIt()
    {
        using var site = LocalSite.SigningIn(Inside, Anna, "Lab");
        // The page is in at once; its script comes in pieces for six seconds, and the page is loading until it has.
        using var loading = new LocalSite("<title>loading</title><body><script src=\"/download/slow.js\"></script></body>",
            512 * 1024, 200);
        string[] endedBeforeAsked = null, endedAfter = null;
        WebViewSignInRequest asked = null;
        bool? ofShownAtTheEnd = null, goingOnAtTheEnd = null, ofShownOnceIn = null, goingOnOnceIn = null;
        int? askedBySite = null, askedByAnother = null, begun = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>start</title>", (browser, view) =>
        {
            view.AnswerSignIn = request =>
            {
                endedBeforeAsked = view.Completed.ToArray();
                asked = request;
                // While the site is gone to: its own origin asks of that navigation, another of the page shown.
                begun = browser.NavigationsBegun;
                askedBySite = browser.NavigationAsking(site.Origin);
                askedByAnother = browser.NavigationAsking("http://127.0.0.1:1");
                return true;
            };
            browser.Navigate(loading.Origin + "/");
            Pump(() => view.Title == "loading", TimeSpan.FromSeconds(10));
            browser.Navigate(site.Origin + "/");
            Pump(() => view.Completed.Any(address => address.StartsWith(loading.Origin)), TimeSpan.FromSeconds(15));
            endedAfter = view.Completed.ToArray();
            ofShownAtTheEnd = asked == null ? null : browser.IsOfThePageShown(asked);
            goingOnAtTheEnd = browser.IsGoingOn;

            asked?.Answer(Anna);
            Pump(() => view.Title == "inside", TimeSpan.FromSeconds(10));
            ofShownOnceIn = asked == null ? null : browser.IsOfThePageShown(asked);
            goingOnOnceIn = browser.IsGoingOn;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(endedBeforeAsked, Is.Not.Null, "the site asked");
            Assert.That(endedBeforeAsked, Has.None.StartsWith(loading.Origin), "the page gone on from had not ended yet");
            Assert.That(endedAfter, Has.Some.StartsWith(loading.Origin), "...and ended after the site asked");
            Assert.That(ofShownAtTheEnd, Is.False, "the question is not the ended page's");
            Assert.That(goingOnAtTheEnd, Is.True, "the page shown is one being left");
            Assert.That(ofShownOnceIn, Is.True, "once in, the page that asked is the one shown");
            Assert.That(goingOnOnceIn, Is.False);
            Assert.That(askedBySite, Is.EqualTo(begun), "the site's own origin asks of the navigation to it");
            Assert.That(askedByAnother, Is.LessThan(begun), "another origin - a frame of the page shown - asks of that page");
        });
    }

    // A navigation that comes to no page - here a file, taken as a download - leaves nothing under way once the page has
    // stopped loading: the page shown is the one there is, and its end takes what it asked.
    [Test]
    public void ANavigationToADownload_LeavesThePageShownAsItWas()
    {
        using var site = new LocalSite("<title>files</title>", fileBytes: 10_000);
        bool goingOnWhileGoing = false, goingOnAfter = true;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
        {
            Pump(() => view.Title == "files", TimeSpan.FromSeconds(10));
            browser.Navigate(site.Origin + "/download/report.bin");
            goingOnWhileGoing = browser.IsGoingOn;
            Pump(() => view.Downloads.Count > 0 && !browser.IsGoingOn, TimeSpan.FromSeconds(10));
            goingOnAfter = browser.IsGoingOn;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(goingOnWhileGoing, Is.True, "under way once begun");
            Assert.That(goingOnAfter, Is.False, "the file taken, nothing is under way");
        });
    }
}
