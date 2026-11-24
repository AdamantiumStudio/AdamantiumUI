using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

// The platform Chromium starts on Linux, by the displays there are: X11 while there is an X server, else Wayland, else
// none - with no X server its X11 does not start, nor CEF with it.
[TestFixture]
public class WebViewOzonePlatformTests
{
    [TestCase(":0", "wayland-0", null)]
    [TestCase(":0", null, null)]
    [TestCase(null, "wayland-0", "wayland")]
    [TestCase("", "wayland-0", "wayland")]
    [TestCase(null, null, "headless")]
    [TestCase("", "", "headless")]
    public void ThePlatform_FollowsTheDisplaysThereAre(string display, string waylandDisplay, string expected)
    {
        Assert.That(WebViewEngine.OzonePlatformFor(display, waylandDisplay), Is.EqualTo(expected));
    }
}
