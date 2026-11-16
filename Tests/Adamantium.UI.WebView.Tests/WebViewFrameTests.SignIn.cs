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
