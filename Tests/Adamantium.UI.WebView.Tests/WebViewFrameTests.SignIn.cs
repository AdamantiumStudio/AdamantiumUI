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
}
