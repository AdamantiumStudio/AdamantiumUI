using System;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

[TestFixture]
public class WebViewBridgeTests
{
    [Test]
    public void ScriptsForNewDocuments_AreHeldInTheOrderAdded_UntilTakenAway()
    {
        var bridge = new WebViewBridge();
        var first = bridge.AddScriptToNewDocument("window.a = 1");
        var second = bridge.AddScriptToNewDocument("window.b = 2");

        Assert.Multiple(() =>
        {
            Assert.That(bridge.ScriptsForNewDocuments, Is.EqualTo(new[] { first, second }));
            Assert.That(first.Source, Is.EqualTo("window.a = 1"));
            Assert.That(bridge.RemoveScriptFromNewDocument(first), Is.True);
            Assert.That(bridge.RemoveScriptFromNewDocument(first), Is.False, "taken away once");
            Assert.That(bridge.ScriptsForNewDocuments, Is.EqualTo(new[] { second }));
        });
    }

    [Test]
    public void AScriptOfAnotherBridge_IsNotTakenAway()
    {
        var bridge = new WebViewBridge();
        var kept = bridge.AddScriptToNewDocument("window.a = 1");
        var other = new WebViewBridge().AddScriptToNewDocument("window.a = 1");

        Assert.Multiple(() =>
        {
            Assert.That(bridge.RemoveScriptFromNewDocument(other), Is.False);
            Assert.That(bridge.ScriptsForNewDocuments, Is.EqualTo(new[] { kept }));
        });
    }

    [Test]
    public void NoScript_IsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new WebViewBridge().AddScriptToNewDocument(null));
    }

    // A page that closed itself is told with where it was and what it was called - a browser keeps the tab to bring back
    // by them - though the view has let go of both: the same address given again opens it again.
    [Test]
    public void APageThatClosedItself_IsToldWithItsAddressAndTitle()
    {
        var address = new Uri("https://example.com/closes-itself");
        var bridge = new WebViewBridge();
        var view = new WebView { Source = address, Bridge = bridge };
        view.SetCurrentValue(WebView.TitleProperty, "Closes itself");
        WebPageClosedEventArgs told = null;
        bridge.PageClosed += (_, e) => told = e;

        // No browser made: the view's own, null, is the one closing.
        ((Cef.IWebViewBrowserView)view).PageClosed(null);

        Assert.Multiple(() =>
        {
            Assert.That(told?.Address, Is.EqualTo(address));
            Assert.That(told?.Title, Is.EqualTo("Closes itself"));
            Assert.That(view.Source, Is.Null);
        });
    }

    // Told last: an address the application gives the view then stands.
    [Test]
    public void AnAddressGivenWhenThePageClosedItself_Stands()
    {
        var home = new Uri("https://example.com/home");
        var bridge = new WebViewBridge();
        var view = new WebView { Source = new Uri("https://example.com/closes-itself"), Bridge = bridge };
        bridge.PageClosed += (_, _) => view.Source = home;

        ((Cef.IWebViewBrowserView)view).PageClosed(null);

        Assert.That(view.Source, Is.EqualTo(home));
    }
}
