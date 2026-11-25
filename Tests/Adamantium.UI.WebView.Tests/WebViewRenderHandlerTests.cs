using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

// CEF asks the render handler for the accessibility handler with each change of the page's tree - a closing page's too,
// after its browser let go of its own handlers. The one handed over must still be there then.
[TestFixture]
public unsafe class WebViewRenderHandlerTests
{
    [Test]
    public void TheAccessibilityHandler_StaysWhileCefHoldsTheRenderHandler_AfterTheBrowserLetGo()
    {
        var accessibility = new WebViewAccessibilityHandler(null!);
        var render = new WebViewRenderHandler(null!, accessibility);
        var heldByCef = (CefRenderHandler*)render.Share();
        render.Release();
        accessibility.Release();

        var handed = heldByCef->get_accessibility_handler(heldByCef);
        var found = CefObject.From<WebViewAccessibilityHandler>(handed);
        CefRefCounting.ReleaseCef(handed);
        var stillThere = accessibility.HasReferences;
        CefRefCounting.ReleaseCef(heldByCef);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.SameAs(accessibility));
            Assert.That(stillThere, Is.True);
            Assert.That(accessibility.HasReferences, Is.False, "let go of once CEF let go of the render handler");
        });
    }
}
