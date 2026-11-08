using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Multiverse;
using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

/// <summary>
/// A page drawn by the real CEF reaches the surface the view's panel shows, along each path a frame takes: pixels in
/// memory written into it, and Chromium's own texture on the same GPU copied there. One CEF for the run, on one thread,
/// as in an application. Runs where CEF is staged (native/fetch-cef); CI does not download it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class WebViewFrameTests
{
    private const int Width = 64;
    private const int Height = 48;
    private const string RedPage = "data:text/html,<html style='background:%23ff0000'></html>";

    private CefTestThread loop;
    private IGraphicsDeviceService devices;
    private WebViewEngine engine;
    private string profile;

    [OneTimeSetUp]
    public void StartCef()
    {
        if (!File.Exists(CefLibrary.LibraryFile(CefLibrary.ApplicationFolder)))
        {
            Assert.Ignore($"CEF is not staged in {CefLibrary.ApplicationFolder}: run Adamantium.UI.WebView/native/fetch-cef.");
        }

        profile = Path.Combine(Path.GetTempPath(), "AdamantiumWebViewTests", Guid.NewGuid().ToString("N"));
        loop = new CefTestThread();
        loop.Run(() =>
        {
            devices = new GraphicsDeviceService(new GraphicsDeviceFactory(), false, GraphicsSettings.CreateDefault());
            devices.CreateMainDevice("WebViewFrameTests");
            engine = new WebViewEngine(devices, null, new WebViewOptions { ProfileFolder = profile });
        });
    }

    [OneTimeTearDown]
    public void StopCef()
    {
        loop?.Run(() =>
        {
            engine?.Dispose();
            devices?.MainGraphicsDevice?.DeviceWaitIdle();
            devices?.MainGraphicsDevice?.FlushRetiredAfterIdle();
            devices?.MainGraphicsDevice?.Dispose();
        });
        loop?.Dispose();
        try
        {
            Directory.Delete(profile, true);
        }
        catch (IOException)
        {
        }
    }

    [TestCase(WebViewFramePath.PixelBuffer)]
    [TestCase(WebViewFramePath.SharedTexture)]
    public void APageOfOneColor_ComesIntoTheSurface(WebViewFramePath path)
    {
        var (madePath, frames, refused, center) = loop.Run(() => ShowPage(path));

        if (path == WebViewFramePath.SharedTexture)
        {
            Assume.That(madePath, Is.EqualTo(path), "the device does not import Chromium's textures");
        }

        Assert.Multiple(() =>
        {
            Assert.That(madePath, Is.EqualTo(path));
            Assert.That(refused, Is.False, "the device took Chromium's texture");
            Assert.That(frames, Is.GreaterThan(0), "a frame came");
            Assert.That(center, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "red, as BGRA, in the middle");
        });
    }

    // A page that keeps drawing and takes its time to go, as a busy site does: CEF calls the closing browser's handlers
    // meanwhile, and they are to be there - the browser made next is to get its frames.
    [TestCase(WebViewFramePath.PixelBuffer)]
    [TestCase(WebViewFramePath.SharedTexture)]
    public void ABrowserMadeWhileTheLastOneStillCloses_GetsItsFrames(WebViewFramePath path)
    {
        const string slowToGo = "data:text/html,<html style='background:%2300ff00'><body><div id=d>.</div><script>" +
                                "setInterval(()=>d.textContent+='.',16);" +
                                "addEventListener('unload',()=>{const t=Date.now();while(Date.now()-t<300){}});" +
                                "</script></body></html>";
        var (madePath, frames, refused, center) = loop.Run(() =>
        {
            var first = engine.CreateBrowser(path == WebViewFramePath.SharedTexture, _ => { }, () => { }, () => { },
                _ => { }, () => { });
            first.Create(slowToGo, Width, Height, 1, IntPtr.Zero);
            Pump(() => false, TimeSpan.FromSeconds(2));
            first.Dispose();
            return ShowPage(path);
        });

        if (path == WebViewFramePath.SharedTexture)
        {
            Assume.That(madePath, Is.EqualTo(path), "the device does not import Chromium's textures");
        }

        Assert.Multiple(() =>
        {
            Assert.That(refused, Is.False);
            Assert.That(frames, Is.GreaterThan(0), "the next browser drew");
            Assert.That(center, Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
        });
    }

    private (WebViewFramePath Path, int Frames, bool Refused, byte[] Center) ShowPage(WebViewFramePath path)
    {
        SharedSurfaceDescriptor shown = null;
        var frames = 0;
        var refused = false;
        var browser = engine.CreateBrowser(path == WebViewFramePath.SharedTexture, descriptor => shown = descriptor,
            () => shown = null, () => frames++, _ => { }, () => refused = true);
        try
        {
            if (browser.FramePath != path)
            {
                return (browser.FramePath, 0, false, null);
            }

            browser.Create(RedPage, Width, Height, 1, IntPtr.Zero);
            Pump(() => frames > 0 || refused, TimeSpan.FromSeconds(30));
            Pump(() => false, TimeSpan.FromMilliseconds(500));
            return (browser.FramePath, frames, refused, shown == null ? null : CenterOfNewest(shown));
        }
        finally
        {
            browser.Dispose();
            Pump(() => false, TimeSpan.FromMilliseconds(300));
        }
    }

    private static void Pump(Func<bool> done, TimeSpan timeout)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!done() && Environment.TickCount64 < until)
        {
            LoopSignal.Drain();
            Thread.Sleep(5);
        }
    }

    private byte[] CenterOfNewest(SharedSurfaceDescriptor descriptor)
    {
        var consumer = devices.CreateRenderDevice();
        try
        {
            using var taken = SharedSurface.Import(consumer, descriptor);
            taken.LatchCopy(consumer);
            consumer.DeviceWaitIdle();
            using var image = taken.LatchedCopy.ToImage();
            var pixel = new byte[4];
            Marshal.Copy(image.DataPointer + (int)((descriptor.Height / 2 * descriptor.Width + descriptor.Width / 2) * 4),
                pixel, 0, 4);
            return pixel;
        }
        finally
        {
            consumer.Dispose();
        }
    }
}
