using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

/// <summary>
/// The profile's cookies through the engine: one the application sets is read back as set and deleted; one a page
/// sets is read by the application, and one the application sets is read by the page; a cookie Chromium refuses is not
/// set.
/// </summary>
public partial class WebViewFrameTests
{
    private static readonly Uri CookieSite = new("https://cookies.test/shop/");

    [Test]
    public void ACookieTheApplicationSets_IsReadAsSet_AndDeleted()
    {
        var expires = DateTimeOffset.UtcNow.AddDays(1);
        var cookie = new WebCookie
        {
            Name = "kept",
            Value = "one",
            Path = "/shop",
            IsSecure = true,
            IsHttpOnly = true,
            SameSite = WebCookieSameSite.Strict,
            Expires = expires
        };
        bool set = false;
        IReadOnlyList<WebCookie> forSite = null, all = null, afterwards = null;
        var deleted = 0;
        loop.Run(() =>
        {
            set = Finish(engine.SetCookieAsync(CookieSite, cookie));
            forSite = Finish(engine.GetCookiesAsync(CookieSite));
            all = Finish(engine.GetCookiesAsync());
            deleted = Finish(engine.DeleteCookiesAsync(CookieSite, "kept"));
            afterwards = Finish(engine.GetCookiesAsync(CookieSite));
        });

        var read = forSite?.SingleOrDefault(found => found.Name == "kept");
        Assert.Multiple(() =>
        {
            Assert.That(set, Is.True);
            Assert.That(read, Is.Not.Null, "read back for its site");
            Assert.That(read?.Value, Is.EqualTo("one"));
            Assert.That(read?.Path, Is.EqualTo("/shop"));
            Assert.That(read?.Domain, Is.EqualTo("cookies.test"), "a host cookie is read as its host");
            Assert.That(read?.IsSecure, Is.True);
            Assert.That(read?.IsHttpOnly, Is.True, "a script-hidden cookie is read too");
            Assert.That(read?.SameSite, Is.EqualTo(WebCookieSameSite.Strict));
            Assert.That(read?.Expires, Is.EqualTo(expires).Within(TimeSpan.FromSeconds(1)));
            Assert.That(read?.Created, Is.Not.Null, "Chromium tells when it was made");
            Assert.That(all?.Any(found => found.Name == "kept"), Is.True, "and among all of them");
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(afterwards?.Any(found => found.Name == "kept"), Is.False);
        });
    }

    // A page's cookie reaches the application, and the application's reaches the page.
    [Test]
    public void ThePageAndTheApplication_SeeEachOthersCookies()
    {
        using var site = new LocalSite("<title>cookies</title><script>document.cookie = 'fromPage=seen; path=/'</script>");
        var origin = new Uri(site.Origin + "/");
        IReadOnlyList<WebCookie> read = null;
        string seenByPage = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, origin.AbsoluteUri, (browser, view) =>
        {
            Pump(() => view.Completed.Count > 0, TimeSpan.FromSeconds(10));
            read = Finish(engine.GetCookiesAsync(origin));
            Finish(engine.SetCookieAsync(origin, new WebCookie { Name = "fromApp", Value = "given", Path = "/" }));
            seenByPage = Await(browser.EvaluateAsync("document.cookie"));
        }));
        loop.Run(() => Finish(engine.DeleteCookiesAsync(origin)));

        Assert.Multiple(() =>
        {
            Assert.That(read?.SingleOrDefault(found => found.Name == "fromPage")?.Value, Is.EqualTo("seen"));
            Assert.That(seenByPage, Does.Contain("fromApp=given"));
        });
    }

    // Chromium refuses a value with ';' in it - it would end the cookie - and sets nothing.
    [Test]
    public void ACookieChromiumRefuses_IsNotSet()
    {
        var set = true;
        IReadOnlyList<WebCookie> read = null;
        loop.Run(() =>
        {
            set = Finish(engine.SetCookieAsync(CookieSite, new WebCookie { Name = "broken", Value = "a;b" }));
            read = Finish(engine.GetCookiesAsync(CookieSite));
        });

        Assert.Multiple(() =>
        {
            Assert.That(set, Is.False);
            Assert.That(read?.Any(found => found.Name == "broken"), Is.False);
        });
    }

    // A host cookie read and set again with another value stays a host cookie, not one of its domain; a path left
    // empty is the address's own directory.
    [Test]
    public void AHostCookieReadAndSetAgain_StaysAHostCookie()
    {
        IReadOnlyList<WebCookie> again = null;
        loop.Run(() =>
        {
            Finish(engine.SetCookieAsync(CookieSite, new WebCookie { Name = "host", Value = "one" }));
            var read = Finish(engine.GetCookiesAsync(CookieSite)).Single(found => found.Name == "host");
            Finish(engine.SetCookieAsync(CookieSite, read with { Value = "two" }));
            again = Finish(engine.GetCookiesAsync(CookieSite));
            Finish(engine.DeleteCookiesAsync(CookieSite, "host"));
        });

        var host = again?.Where(found => found.Name == "host").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(host?.Select(found => found.Value), Is.EqualTo(new[] { "two" }), "one cookie, given again");
            Assert.That(host?.Single().Domain, Is.EqualTo("cookies.test"), "still the host's own");
            Assert.That(host?.Single().Path, Is.EqualTo("/shop"), "the address's directory");
        });
    }

    // A name with no address would delete every cookie, as CEF reads it: refused.
    [Test]
    public void DeletingANameWithNoAddress_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => engine.DeleteCookiesAsync(name: "kept"));
    }

    // Before the main graphics device is made CEF is not started for cookies - it starts on that device's adapter -
    // and the engine is not given up on.
    [Test]
    public void CookiesAskedBeforeTheMainDevice_AreNone_AndTheEngineStaysUsable()
    {
        using var early = new WebViewEngine(new NoDevices(), null, new WebViewOptions { ProfileFolder = profile });
        IReadOnlyList<WebCookie> read = null;
        var set = true;
        loop.Run(() =>
        {
            read = Finish(early.GetCookiesAsync());
            set = Finish(early.SetCookieAsync(CookieSite, new WebCookie { Name = "early" }));
        });

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.Empty);
            Assert.That(set, Is.False);
            Assert.That(early.IsAvailable, Is.True, "not given up on: it starts once the device is made");
        });
    }

    private static T Finish<T>(Task<T> answer)
    {
        Pump(() => answer.IsCompleted, TimeSpan.FromSeconds(10));
        Assert.That(answer.IsCompletedSuccessfully, Is.True, "the cookie call answered");
        return answer.Result;
    }
}
