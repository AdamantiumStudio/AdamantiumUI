using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

public partial class WebViewFrameTests
{
    // A page never gone on from may close itself, as Chromium lets it: CEF lets the browser go, and the view is told once.
    [Test]
    public void APageThatClosesItself_IsToldToTheView()
    {
        var closed = -1;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer,
            "data:text/html,<body><script>setTimeout(()=>window.close(),300)</script></body>",
            (browser, view) => Pump(() => view.ClosedItself > 0, System.TimeSpan.FromSeconds(5), view),
            view => closed = view.ClosedItself));

        Assert.That(closed, Is.EqualTo(1));
    }
}
