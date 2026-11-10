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
}
