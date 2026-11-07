using System;
using System.IO;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

// The generated bindings against the real libcef: the library loads from cef/ with the API hash its headers name, is
// the CEF version they come from, and reads the process's arguments - with no --type the caller is the browser process,
// which cef_execute_process answers with -1. Runs where CEF is staged (native/fetch-cef); CI does not download it.
[TestFixture]
public class CefLibraryTests
{
    private const int CefMajorEntry = 0;
    private const int ChromeMajorEntry = 4;

    [SetUp]
    public void RequireStagedCef()
    {
        if (!File.Exists(CefLibrary.LibraryFile(CefLibrary.ApplicationFolder)))
        {
            Assert.Ignore($"CEF is not staged in {CefLibrary.ApplicationFolder}: run Adamantium.UI.WebView/native/fetch-cef.");
        }
    }

    [Test]
    public void LoadsAtTheGeneratedApiVersion()
    {
        CefLibrary.Load(CefLibrary.ApplicationFolder);

        Assert.That(CefInterop.CefApiVersion(), Is.EqualTo(CefLibrary.ApiVersion));
    }

    [Test]
    public void IsTheCefVersionOfTheHeaders()
    {
        CefLibrary.Load(CefLibrary.ApplicationFolder);

        Assert.That(CefInterop.CefVersionInfo(CefMajorEntry), Is.EqualTo(CefLibrary.CefVersionMajor));
        Assert.That(CefInterop.CefVersionInfo(ChromeMajorEntry), Is.EqualTo(CefLibrary.CefVersionMajor));
    }

    [Test]
    public void ArgumentsWithoutAProcessTypeAreTheBrowserProcess()
    {
        var exitCode = CefSubprocess.Run(CefLibrary.ApplicationFolder, [Environment.ProcessPath ?? "test"]);

        Assert.That(exitCode, Is.EqualTo(-1));
    }
}
