using System;
using Adamantium.UI.WebView.Cef;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

// The ids our CEF build lets register: none unless the application names it, so Chromium fetches nothing unasked.
[TestFixture]
public class CefComponentsTests
{
    // Data, not code, is fetched unless the application says otherwise: the hyphenation dictionaries and the list of
    // revoked certificates; Google's Widevine only when named.
    [Test]
    public void AWebViewByDefault_FetchesHyphenationAndRevokedCertificates_NotWidevine()
    {
        Assert.That(new WebViewOptions().Components,
            Is.EqualTo(WebViewComponents.Hyphenation | WebViewComponents.RevokedCertificates));
    }

    [Test]
    public void NoComponent_LetsNoneRegister()
    {
        Assert.That(CefComponents.Allowed(WebViewComponents.None), Is.Empty);
    }

    [Test]
    public void TheComponentsNamed_AreListedByTheirIds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CefComponents.Allowed(WebViewComponents.Widevine),
                Is.EqualTo("oimompecagnajdejgnnjijobebaeigek"));
            Assert.That(
                CefComponents.Allowed(WebViewComponents.Widevine | WebViewComponents.Hyphenation |
                                      WebViewComponents.RevokedCertificates),
                Is.EqualTo("oimompecagnajdejgnnjijobebaeigek,jamhcnnkihinmdlkakkaopbjbbcngflc," +
                           "hfnkpimlhhgieaddgfemjhofmfblmnib"));
        });
    }

    [Test]
    public void AComponentIsAskedAbout_OneAtATime()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CefComponents.IdOf(WebViewComponents.Hyphenation),
                Is.EqualTo("jamhcnnkihinmdlkakkaopbjbbcngflc"));
            Assert.Throws<ArgumentException>(() =>
                CefComponents.IdOf(WebViewComponents.Widevine | WebViewComponents.Hyphenation));
            Assert.Throws<ArgumentException>(() => CefComponents.IdOf(WebViewComponents.None));
        });
    }
}
