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
                // Chromium registers its components once the profile is up, each after a look at its folder.
                Pump(() => updater->get_component_count(updater) > 0, TimeSpan.FromSeconds(10));
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
    // from then on. Without it a page is told the key system is not supported. On Linux Chromium loads it only as it
    // starts: fetched while running, a page has it from the next start.
    [Test]
    public void Widevine_AskedFor_IsInstalled_AndPagesHaveItFromThen()
    {
        string before = null;
        string after = null;
        var installedAtStart = false;
        WebComponentStatus installed = null;
        WebComponentStatus asked = null;
        using var site = new LocalSite("<title>Protected</title>");
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site.Origin + "/", (browser, view) =>
        {
            Pump(() => view.Title == "Protected", TimeSpan.FromSeconds(5));
            installedAtStart = engine.GetComponentStatus(WebViewComponents.Widevine).IsInstalled;
            before = installedAtStart ? null : Await(browser.EvaluateAsync(AskWidevine));
            var install = engine.InstallComponentAsync(WebViewComponents.Widevine);
            Pump(() => install.IsCompleted, TimeSpan.FromSeconds(60));
            installed = install.IsCompletedSuccessfully ? install.Result : null;
            asked = engine.GetComponentStatus(WebViewComponents.Widevine);
            after = Await(browser.EvaluateAsync(AskWidevine));
        }));

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Null.Or.EqualTo("\"NotSupportedError\""), "before it is installed");
            Assert.That(installed?.IsInstalled, Is.True, "installed");
            Assert.That(Version.TryParse(installed?.Version, out _), Is.True, $"a version: {installed?.Version}");
            Assert.That(asked, Is.EqualTo(installed), "the status asked after");
            Assert.That(after, Is.EqualTo(OperatingSystem.IsLinux() && !installedAtStart
                ? "\"NotSupportedError\""
                : "\"com.widevine.alpha\""));
        });
    }
}
