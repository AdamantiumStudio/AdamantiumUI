using Adamantium.UI.WebView.Cef;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

public partial class WebViewFrameTests
{
    // H.264 and AAC are told playable exactly when the FFmpeg beside CEF decodes them - our build with the codecs
    // package; without it they are refused, as in CEF's own build, and a page falls back to WebM. VP9 plays either way.
    [Test]
    public void H264AndAac_AreToldPlayable_OnlyWithTheCodecsFFmpeg()
    {
        const string ask = "['video/mp4; codecs=\"avc1.4d401e\"', 'audio/mp4; codecs=\"mp4a.40.2\"', " +
            "'video/webm; codecs=\"vp9\"'].map(type => document.createElement('video').canPlayType(type)).join(',')";
        var expected = ProprietaryCodecs.IsInstalled(CefLibrary.ApplicationFolder) ? "probably" : "";
        string answer = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, "data:text/html,<title>Codecs</title>", (browser, view) =>
        {
            answer = Await(browser.EvaluateAsync(ask));
        }));

        Assert.That(answer, Is.EqualTo($"\"{expected},{expected},probably\""));
    }
}
