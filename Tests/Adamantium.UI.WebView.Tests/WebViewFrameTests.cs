using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Multiverse;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Cef.Interop;
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
            var first = engine.CreateBrowser(path == WebViewFramePath.SharedTexture, new RecordingBrowserView());
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

    // A click on the page reaches it: its handler paints the page green.
    [Test]
    public void AClick_ReachesThePage()
    {
        const string page = "data:text/html,<html style='height:100%;background:%23ff0000' " +
                            "onclick=\"this.style.background='%2300ff00'\"></html>";
        var (_, _, _, center) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            browser.SendMouseMove(Width / 2, Height / 2, InputModifiers.None, false);
            browser.SendMouseClick(Width / 2, Height / 2, InputModifiers.LeftMouseButton, CefMouseButtonType.Left, false, 1);
            browser.SendMouseClick(Width / 2, Height / 2, InputModifiers.None, CefMouseButtonType.Left, true, 1);
        }));

        Assert.That(center, Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "green, as BGRA");
    }

    // The wheel scrolls the page: a red band above a long green one goes up and out of the middle.
    [Test]
    public void TheWheel_ScrollsThePage()
    {
        const string page = "data:text/html,<body style='margin:0'><div style='height:48px;background:%23ff0000'></div>" +
                            "<div style='height:4000px;background:%2300ff00'></div></body>";
        var (_, _, _, center) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            browser.SendMouseMove(Width / 2, Height / 2, InputModifiers.None, false);
            browser.SendMouseWheel(Width / 2, Height / 2, InputModifiers.None, 0, -360);
        }));

        Assert.That(center, Is.EqualTo(new byte[] { 0, 255, 0, 255 }), "green, scrolled to");
    }

    // The cursor the page asks for over a link comes to the view as ours.
    [Test]
    public void ThePagesCursor_ComesToTheView()
    {
        const string page = "data:text/html,<html style='height:100%;cursor:pointer'></html>";
        Cursor cursor = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
            browser.SendMouseMove(Width / 2, Height / 2, InputModifiers.None, false), view => cursor = view.Cursor));

        Assert.That(cursor, Is.SameAs(Cursors.Hand));
    }

    // A key typed into a field of the page: the field takes the character, the page sees the key where it is on the
    // keyboard (KeyboardEvent.code, from the scan code), and the browser says its focus is in a field.
    [Test]
    public void AKeyTyped_GoesIntoTheField_AsTheKeyItIs()
    {
        const string page = "data:text/html,<body style='margin:0'><input id=f style='width:100%;height:48px;" +
                            "border:0;background:%23ff0000;color:%23ff0000' onkeydown=\"window.code=event.code\" " +
                            "oninput=\"f.style.background=f.value=='a'?(window.code=='KeyA'?'%2300ff00':'%230000ff'):'%23ffff00'\"></body>";
        var (_, _, _, center) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            ClickTheMiddle(browser);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            browser.SendKey(Key.A, KeyA, InputModifiers.None, false, false);
            browser.SendCharacter('a', InputModifiers.None);
            browser.SendKey(Key.A, KeyA, InputModifiers.None, true, false);
        }));

        Assert.That(center, Is.EqualTo(new byte[] { 0, 255, 0, 255 }),
            $"green: 'a' typed, code KeyA (blue: another code, yellow: another value) - {string.Join(",", center ?? [])}");
    }

    // The browser says when the page's focus leaves a field: a click past it, after one into it.
    [Test]
    public void AClickPastAField_TellsTheFocusLeftIt()
    {
        const string page = "data:text/html,<body style='margin:0'><input style='width:100%;height:16px;border:0'>" +
                            "<div style='height:32px'></div></body>";
        bool? inField = null;
        bool? pastIt = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            Click(browser, Width / 2, 8);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            inField = browser.InEditableField;
            Click(browser, Width / 2, 40);
            Pump(() => !browser.InEditableField, TimeSpan.FromSeconds(2));
            pastIt = browser.InEditableField;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(inField, Is.True, "in the field");
            Assert.That(pastIt, Is.False, "past it");
        });
    }

    // Text an input method composed goes into the field in place of the composition.
    [Test]
    public void TextAnInputMethodComposed_GoesIntoTheField()
    {
        const string page = "data:text/html;charset=utf-8,<body style='margin:0'><input id=f style='width:100%;" +
                            "height:48px;border:0;background:%23ff0000;color:%23ff0000' " +
                            "oninput=\"f.style.background=f.value=='%E4%BD%A0'?'%2300ff00':'%23ffff00'\"></body>";
        var (_, _, _, center) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            ClickTheMiddle(browser);
            Pump(() => browser.InEditableField, TimeSpan.FromSeconds(2));
            browser.SetComposition("ni", 2);
            Pump(() => false, TimeSpan.FromMilliseconds(200));
            browser.CommitComposition("你");
        }));

        Assert.That(center, Is.EqualTo(new byte[] { 0, 255, 0, 255 }),
            $"green: the composed text is in the field (yellow: other text) - {string.Join(",", center ?? [])}");
    }

    // A finger on the page reaches it as a touch, not as a mouse.
    [Test]
    public void AFinger_ReachesThePageAsATouch()
    {
        const string page = "data:text/html,<html style='height:100%;background:%23ff0000;touch-action:none' " +
                            "onpointerdown=\"this.style.background=event.pointerType=='touch'?'%2300ff00':'%230000ff'\">" +
                            "</html>";
        var (_, _, _, center) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            var properties = new PointerPointProperties { Pressure = 0.5f, ContactRect = new Rect(0, 0, 8, 8) };
            browser.SendTouch(1, Width / 2, Height / 2, CefTouchEventType.Pressed, CefPointerType.Touch, properties,
                InputModifiers.None);
            browser.SendTouch(1, Width / 2, Height / 2, CefTouchEventType.Released, CefPointerType.Touch, properties,
                InputModifiers.None);
        }));

        Assert.That(center, Is.EqualTo(new byte[] { 0, 255, 0, 255 }),
            $"green: a touch (blue: another pointer) - {string.Join(",", center ?? [])}");
    }

    // Text dropped on the page reaches its drop handler, and the page says it takes it.
    [Test]
    public void TextDroppedOnThePage_ReachesIt()
    {
        const string page = "data:text/html,<body style='margin:0'><div id=d style='height:48px;background:%23ff0000' " +
                            "ondragover=\"event.preventDefault()\" ondrop=\"event.preventDefault();d.style.background=" +
                            "event.dataTransfer.getData('text')=='hello'?'%2300ff00':'%230000ff'\"></div></body>";
        var effects = DragDropEffects.None;
        var (_, _, _, center) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            var data = new DataPackage();
            data.Set(DataFormats.Text, "hello");
            var allowed = DragDropEffects.Copy | DragDropEffects.Move;
            browser.DragEnter(data, Width / 2, Height / 2, InputModifiers.None, allowed);
            browser.DragOver(Width / 2, Height / 2, InputModifiers.None, allowed);
            Pump(() => browser.DragEffects != DragDropEffects.None, TimeSpan.FromSeconds(2));
            effects = browser.DragEffects;
            browser.Drop(Width / 2, Height / 2, InputModifiers.None);
        }));

        Assert.Multiple(() =>
        {
            Assert.That(effects, Is.Not.EqualTo(DragDropEffects.None), "the page takes the drag");
            Assert.That(center, Is.EqualTo(new byte[] { 0, 255, 0, 255 }),
                $"green: 'hello' dropped (blue: other text) - {string.Join(",", center ?? [])}");
        });
    }

    // A drag the page starts comes to the view with what the page put in it.
    [Test]
    public void ADragThePageStarts_ComesToTheView()
    {
        const string page = "data:text/html,<body style='margin:0'><div draggable=true style='height:48px;" +
                            "background:%23ff0000' ondragstart=\"event.dataTransfer.setData('text/plain','from page')\">" +
                            "</div></body>";
        DataPackage dragged = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, browser =>
        {
            browser.SendMouseMove(10, Height / 2, InputModifiers.None, false);
            browser.SendMouseClick(10, Height / 2, InputModifiers.LeftMouseButton, CefMouseButtonType.Left, false, 1);
            for (var x = 12; x <= 50; x += 4)
            {
                browser.SendMouseMove(x, Height / 2, InputModifiers.LeftMouseButton, false);
                Pump(() => false, TimeSpan.FromMilliseconds(20));
            }
        }, view => dragged = view.Dragged));

        Assert.That(dragged?.Get(DataFormats.Text), Is.EqualTo("from page"));
    }

    private const uint KeyA = 0x0007_0004;

    private static void ClickTheMiddle(WebViewBrowser browser) => Click(browser, Width / 2, Height / 2);

    private static void Click(WebViewBrowser browser, double x, double y)
    {
        browser.SetFocus(true);
        browser.SendMouseMove(x, y, InputModifiers.None, false);
        browser.SendMouseClick(x, y, InputModifiers.LeftMouseButton, CefMouseButtonType.Left, false, 1);
        browser.SendMouseClick(x, y, InputModifiers.None, CefMouseButtonType.Left, true, 1);
    }

    private (WebViewFramePath Path, int Frames, bool Refused, byte[] Center) ShowPage(WebViewFramePath path) =>
        ShowPage(path, RedPage, null);

    private (WebViewFramePath Path, int Frames, bool Refused, byte[] Center) ShowPage(WebViewFramePath path, string page,
        Action<WebViewBrowser> act, Action<RecordingBrowserView> look = null)
    {
        var view = new RecordingBrowserView();
        var browser = engine.CreateBrowser(path == WebViewFramePath.SharedTexture, view);
        var consumer = new FrameConsumer(devices);
        try
        {
            if (browser.FramePath != path)
            {
                return (browser.FramePath, 0, false, null);
            }

            browser.Create(page, Width, Height, 1, IntPtr.Zero);
            Pump(() => view.Frames > 0 || view.Refused, TimeSpan.FromSeconds(30), view, consumer);
            Pump(() => false, TimeSpan.FromMilliseconds(500), view, consumer);
            if (act != null)
            {
                act(browser);
                Pump(() => false, TimeSpan.FromMilliseconds(700), view, consumer);
            }

            look?.Invoke(view);
            consumer.Take(view.Shown);
            return (browser.FramePath, view.Frames, view.Refused, consumer.Center());
        }
        finally
        {
            browser.Dispose();
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            consumer.Dispose();
        }
    }

    private static void Pump(Func<bool> done, TimeSpan timeout, RecordingBrowserView view = null,
        FrameConsumer consumer = null)
    {
        var until = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (!done() && Environment.TickCount64 < until)
        {
            LoopSignal.Drain();
            if (view != null)
            {
                consumer?.Take(view.Shown);
            }

            Thread.Sleep(5);
        }
    }
}
