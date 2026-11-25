using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

// Chromium fetches from Google only the components the options let it - this fixture's, Widevine: the rest of the
// twenty-odd Chromium registers are not there at all. Fetching Widevine needs the network.
public unsafe partial class WebViewFrameTests
{
    private const string AskWidevine =
        "navigator.requestMediaKeySystemAccess('com.widevine.alpha', [{initDataTypes:['cenc'], " +
        "videoCapabilities:[{contentType:'video/webm; codecs=\"vp9\"'}]}])" +
        ".then(access => access.keySystem, error => error.name)";

    [Test]
    public void OnlyTheComponentsTheOptionsLet_AreRegistered()
    {
        var registered = new List<string>();
        loop.Run(() =>
        {
            engine.GetComponentStatus(WebViewComponents.Widevine);
            var updater = CefInterop.CefComponentUpdaterGet();
            try
            {
                // Chromium registers its components once the profile is up, each after a look at its folder: a while
                // more for any the switch failed to keep out to come too.
                Pump(() => updater->get_component_count(updater) > 0, TimeSpan.FromSeconds(10));
                Pump(() => false, TimeSpan.FromSeconds(3));
                var count = updater->get_component_count(updater);
                var components = new CefComponent*[(int)count];
                fixed (CefComponent** items = components)
                {
                    var filled = count;
                    updater->get_components(updater, &filled, items);
                    for (var i = 0; i < (int)filled; i++)
                    {
                        registered.Add(CefStrings.ReadAndFree(items[i]->get_id(items[i])));
                        CefRefCounting.ReleaseCef(items[i]);
                    }
                }
            }
            finally
            {
                CefRefCounting.ReleaseCef(updater);
            }
        });

        Assert.That(registered, Is.EqualTo(new[] { CefComponents.IdOf(WebViewComponents.Widevine) }));
    }

    [Test]
    public void AComponentTheOptionsLeaveOut_IsNeitherInstalledNorFetched()
    {
        WebComponentStatus asked = null;
        Task<WebComponentStatus> install = null;
        loop.Run(() =>
        {
            asked = engine.GetComponentStatus(WebViewComponents.Hyphenation);
            install = engine.InstallComponentAsync(WebViewComponents.Hyphenation);
            Pump(() => install.IsCompleted, TimeSpan.FromSeconds(5));
        });

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(WebComponentStatus.NotInstalled));
            Assert.That(install.IsCompletedSuccessfully, Is.True, "answered at once");
            Assert.That(install.Result, Is.EqualTo(WebComponentStatus.NotInstalled));
        });
    }

    // Asked for, Widevine is fetched at once - not a minute later, when Chromium would get to it - and a page has it
    // from then on. Asked first, it is asked as Chromium starts, before its components registered: the fetch waits for
    // Widevine to register, and needs no page. On Linux Chromium loads Widevine only as it starts: fetched while
    // running - in this fixture's new profile - a page has it from the next start.
    [Test, Explicit("Needs the network: fetches Widevine from Google.")]
    public void Widevine_AskedFor_IsInstalled_AndPagesHaveItFromThen()
    {
        string answer = null;
        WebComponentStatus installed = null;
        WebComponentStatus asked = null;
        var expected = OperatingSystem.IsLinux() ? "\"NotSupportedError\"" : "\"com.widevine.alpha\"";
        using var site = new LocalSite("<title>Protected</title>");
        loop.Run(() =>
        {
            var install = engine.InstallComponentAsync(WebViewComponents.Widevine);
            Pump(() => install.IsCompleted, TimeSpan.FromSeconds(60));
            installed = install.IsCompletedSuccessfully ? install.Result : null;
            asked = engine.GetComponentStatus(WebViewComponents.Widevine);
        });
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
        {
            Pump(() => view.Title == "Protected", TimeSpan.FromSeconds(5));
            // Chromium hands Widevine to its pages as the install ends, a moment apart from telling it ended.
            for (var asking = 0; asking < 5 && answer != expected; asking++)
            {
                answer = Await(browser.EvaluateAsync(AskWidevine));
                if (answer != expected)
                {
                    Pump(() => false, TimeSpan.FromSeconds(1));
                }
            }
        }));

        Assert.Multiple(() =>
        {
            Assert.That(installed?.IsInstalled, Is.True, "installed");
            Assert.That(Version.TryParse(installed?.Version, out _), Is.True, $"a version: {installed?.Version}");
            Assert.That(asked, Is.EqualTo(installed), "the status asked after");
            Assert.That(answer, Is.EqualTo(expected));
        });
    }
}
