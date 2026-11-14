using System.IO;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

// The switches WebView starts Chromium with, on CEF's own command line: the features it enables join the ones the
// application's command line enables already - one --enable-features would otherwise stand in for the other. Runs where
// CEF is staged (native/fetch-cef); CI does not download it.
[TestFixture]
public unsafe class WebViewAppTests
{
    [SetUp]
    public void RequireStagedCef()
    {
        if (!File.Exists(CefLibrary.LibraryFile(CefLibrary.ApplicationFolder)))
        {
            Assert.Ignore($"CEF is not staged in {CefLibrary.ApplicationFolder}: run Adamantium.UI.WebView/native/fetch-cef.");
        }

        CefLibrary.Load(CefLibrary.ApplicationFolder);
    }

    [Test]
    public void EnabledFeatures_JoinTheOnesTheCommandLineEnables()
    {
        var commandLine = CefInterop.CefCommandLineCreate();
        try
        {
            WebViewApp.AppendSwitches(commandLine, [new(WebViewApp.EnableFeaturesSwitch, "First")]);
            var alone = ReadSwitch(commandLine, WebViewApp.EnableFeaturesSwitch);
            WebViewApp.AppendSwitches(commandLine, [new("no-zygote", null), new(WebViewApp.EnableFeaturesSwitch, "Second")]);

            Assert.Multiple(() =>
            {
                Assert.That(alone, Is.EqualTo("First"));
                Assert.That(ReadSwitch(commandLine, WebViewApp.EnableFeaturesSwitch), Is.EqualTo("First,Second"));
                Assert.That(ReadSwitch(commandLine, "no-zygote"), Is.Empty);
            });
        }
        finally
        {
            CefRefCounting.ReleaseCef(commandLine);
        }
    }

    private static string ReadSwitch(CefCommandLine* commandLine, string name)
    {
        var cefName = new CefStringUtf16();
        try
        {
            CefStrings.Set(&cefName, name);
            return CefStrings.ReadAndFree(commandLine->get_switch_value(commandLine, &cefName));
        }
        finally
        {
            CefStrings.Clear(&cefName);
        }
    }
}
