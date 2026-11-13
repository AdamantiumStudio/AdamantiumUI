using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Adamantium.Graphics;
using Adamantium.Graphics.Core;
using Adamantium.Mathematics;
using Adamantium.Multiverse;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Input;
using Adamantium.UI.Core.Media.Imaging;
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
public partial class WebViewFrameTests
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
        if (profile == null)
        {
            return;
        }

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

    // Started ahead, Chromium is kept up by a blank page shown nowhere; the first browser shows its page, and the blank
    // page goes with nothing left behind.
    [Test]
    public void StartedAhead_TheFirstBrowserShowsItsPage_AndTheBlankPageGoes()
    {
        bool settled = false, startedAhead = false, aheadWhileShown = true;
        var browsersAhead = -1;
        var (_, frames, _, center) = loop.Run(() =>
        {
            Pump(() => engine.BrowserCount == 0, TimeSpan.FromSeconds(10));
            settled = engine.BrowserCount == 0;
            engine.StartAhead();
            engine.StartAhead();
            Pump(() => engine.IsStartedAhead, TimeSpan.FromSeconds(30));
            startedAhead = engine.IsStartedAhead;
            browsersAhead = engine.BrowserCount;
            return ShowPage(WebViewFramePath.PixelBuffer, RedPage, _ => aheadWhileShown = engine.IsStartedAhead);
        });
        var left = loop.Run(() =>
        {
            Pump(() => engine.BrowserCount == 0, TimeSpan.FromSeconds(10));
            return engine.BrowserCount;
        });

        Assert.Multiple(() =>
        {
            Assert.That(settled, Is.True, "no browser left from before");
            Assert.That(startedAhead, Is.True, "CEF made the blank page");
            Assert.That(browsersAhead, Is.EqualTo(1), "one blank page, asked for twice");
            Assert.That(aheadWhileShown, Is.False, "the first browser ends the blank page");
            Assert.That(frames, Is.GreaterThan(0));
            Assert.That(center, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "red, as BGRA");
            Assert.That(left, Is.Zero, "the blank page closed too");
        });
    }

    // A page with no background of its own shows white, as in a browser; asked for, a transparent one shows through.
    [Test]
    public void APageWithNoBackground_IsWhite_OrSeeThroughWhenAskedFor()
    {
        const string bare = "data:text/html,<body></body>";
        var (_, _, _, white) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, bare, (Action<WebViewBrowser, RecordingBrowserView>)null));
        var (_, _, _, clear) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, bare,
            (Action<WebViewBrowser, RecordingBrowserView>)null, background: Colors.Transparent));

        Assert.Multiple(() =>
        {
            Assert.That(white, Is.EqualTo(new byte[] { 255, 255, 255, 255 }), "white");
            Assert.That(clear?[3], Is.EqualTo(0), "transparent");
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

    // Out of sight, a page gives its surfaces back - a frame's size each - and seen again it gets new ones and a whole
    // frame in them, the page as it was.
    [TestCase(WebViewFramePath.PixelBuffer)]
    [TestCase(WebViewFramePath.SharedTexture)]
    public void AHiddenPage_GivesItsSurfacesBack_AndDrawsAgainWhenSeen(WebViewFramePath path)
    {
        bool shownBefore = false, shownHidden = true, shownAgain = false;
        int framesHidden = -1, framesAfter = 0;
        var (madePath, _, _, center) = loop.Run(() => ShowPage(path, RedPage, (browser, view) =>
        {
            shownBefore = view.Shown != null;
            browser.SetHidden(true);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            shownHidden = view.Shown != null;
            var framesWhenHidden = view.Frames;
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            framesHidden = view.Frames - framesWhenHidden;

            var framesBeforeSeen = view.Frames;
            browser.SetHidden(false);
            Pump(() => view.Shown != null && view.Frames > framesBeforeSeen, TimeSpan.FromSeconds(5));
            shownAgain = view.Shown != null;
            framesAfter = view.Frames - framesBeforeSeen;
        }));

        if (path == WebViewFramePath.SharedTexture)
        {
            Assume.That(madePath, Is.EqualTo(path), "the device does not import Chromium's textures");
        }

        Assert.Multiple(() =>
        {
            Assert.That(shownBefore, Is.True);
            Assert.That(shownHidden, Is.False, "hidden, its surfaces are let go");
            Assert.That(framesHidden, Is.Zero, "and nothing is written");
            Assert.That(shownAgain, Is.True, "seen again, it has surfaces");
            Assert.That(framesAfter, Is.GreaterThan(0), "and a frame in them");
            Assert.That(center, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "the page as it was, red as BGRA");
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

    // Two pages and back: the address and title come back to the first, and the history says where it can go.
    [Test]
    public void BackFromASecondPage_ComesToTheFirst()
    {
        const string first = "data:text/html,<title>First</title><body>1</body>";
        const string second = "data:text/html,<title>Second</title><body>2</body>";
        string titleOnSecond = null, addressBack = null, titleBack = null;
        bool backOnSecond = false, forwardBack = false;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, first, (browser, view) =>
        {
            Pump(() => view.Title == "First" && !view.IsLoading, TimeSpan.FromSeconds(5));
            browser.Navigate(second);
            Pump(() => view.Title == "Second" && !view.IsLoading, TimeSpan.FromSeconds(5));
            titleOnSecond = view.Title;
            backOnSecond = view.CanGoBack;
            browser.GoBack();
            Pump(() => view.Title == "First" && !view.IsLoading, TimeSpan.FromSeconds(5));
            addressBack = view.Address;
            titleBack = view.Title;
            forwardBack = view.CanGoForward;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(titleOnSecond, Is.EqualTo("Second"));
            Assert.That(backOnSecond, Is.True, "the second page can go back");
            Assert.That(titleBack, Is.EqualTo("First"));
            Assert.That(addressBack, Is.EqualTo(first));
            Assert.That(forwardBack, Is.True, "the first page can go forward again");
        });
    }

    // An address that cannot be reached says why: the net error and the address. It still says so once Chromium's own
    // page for the error has loaded - that load once took the error away - and stops when the page goes elsewhere.
    [Test]
    public void AnAddressThatCannotBeReached_SaysWhyUntilThePageGoesElsewhere()
    {
        const string nowhere = "https://nothing-here.invalid/";
        WebViewLoadError error = null;
        WebViewLoadError afterErrorPage = null;
        WebViewLoadError afterLeaving = new(0, "not yet", null);
        List<string> started = [];
        List<string> completed = [];
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, RedPage, (browser, view) =>
        {
            browser.Navigate(nowhere);
            Pump(() => view.Error != null, TimeSpan.FromSeconds(15));
            error = view.Error;
            var loads = view.LoadsStarted;
            Pump(() => view.LoadsStarted > loads && !view.IsLoading, TimeSpan.FromSeconds(5));
            afterErrorPage = view.Error;

            started = [.. view.Started];
            completed = [.. view.Completed];

            browser.Navigate(RedPage);
            Pump(() => view.Completed.Contains(RedPage), TimeSpan.FromSeconds(15));
            afterLeaving = view.Error;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null, "an error came");
            Assert.That(error?.Code, Is.LessThan(0), "a net error");
            Assert.That(error?.Address, Is.EqualTo(nowhere));
            Assert.That(afterErrorPage, Is.SameAs(error), "the error page loading leaves the error");
            Assert.That(afterLeaving, Is.Null, "going elsewhere ends it");
            Assert.That(started.Count(address => address == nowhere), Is.EqualTo(1),
                "the error page is no navigation of the page's");
            Assert.That(completed.Count(address => address == nowhere), Is.EqualTo(1),
                "the failed address completes once, with its error");
        });
    }

    // The main device made again - as after the GPU lost it: the page lives on and its frames come again, red as before,
    // on surfaces of the new device.
    [TestCase(WebViewFramePath.PixelBuffer)]
    [TestCase(WebViewFramePath.SharedTexture)]
    public void ThePage_OutlivesTheMainDeviceMadeAgain(WebViewFramePath path)
    {
        byte[] before = null, after = null;
        SharedSurfaceDescriptor shownBefore = null, shownAfter = null;
        var titleKept = false;
        var pathTaken = path;
        loop.Run(() =>
        {
            var view = new RecordingBrowserView();
            var browser = engine.CreateBrowser(path == WebViewFramePath.SharedTexture, view);
            pathTaken = browser.FramePath;
            if (pathTaken != path)
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
                return;
            }

            var consumer = new FrameConsumer(devices);
            try
            {
                browser.Create("data:text/html,<title>Kept</title><html style='background:%23ff0000'></html>", Width,
                    Height, 1, IntPtr.Zero);
                // A slow device's first frame may still be the white before the page's background.
                Pump(() => view.Title == "Kept" && IsRed(consumer.Center()), TimeSpan.FromSeconds(30), view, consumer);
                consumer.Take(view.Shown);
                before = consumer.Center();
                shownBefore = view.Shown;

                consumer.Dispose();
                devices.ChangeOrCreateMainDevice("WebViewFrameTests", true);
                consumer = new FrameConsumer(devices);
                var frames = view.Frames;
                Pump(() => view.Frames > frames && view.Shown != null, TimeSpan.FromSeconds(10), view, consumer);
                Pump(() => false, TimeSpan.FromMilliseconds(300), view, consumer);
                consumer.Take(view.Shown);
                after = consumer.Center();
                shownAfter = view.Shown;
                titleKept = view.Title == "Kept";
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
                consumer.Dispose();
            }
        });

        if (pathTaken != path)
        {
            Assert.Ignore($"This device takes the {pathTaken} path, not the {path} one.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "red before, as BGRA");
            Assert.That(shownAfter, Is.Not.Null.And.Not.SameAs(shownBefore), "new surfaces on the new device");
            Assert.That(after, Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "red again after");
            Assert.That(titleKept, Is.True, "the same page");
        });
    }

    // An address asked for before the browser is made is gone to once it is.
    [Test]
    public void AnAddressAskedForBeforeTheBrowserIsMade_IsGoneTo()
    {
        const string later = "data:text/html,<title>Later</title>";
        string title = null;
        loop.Run(() =>
        {
            var view = new RecordingBrowserView();
            var browser = engine.CreateBrowser(false, view);
            try
            {
                browser.Create(RedPage, Width, Height, 1, IntPtr.Zero);
                browser.Navigate(later);
                Pump(() => view.Title == "Later", TimeSpan.FromSeconds(15));
                title = view.Title;
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.That(title, Is.EqualTo("Later"));
    }

    // Going to the address the page is at loads it again, as a browser does on Enter.
    [Test]
    public void GoingToTheAddressThePageIsAt_LoadsItAgain()
    {
        var starts = 0;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, RedPage, (browser, view) =>
        {
            var before = view.Started.Count;
            browser.Navigate(RedPage);
            Pump(() => view.Started.Count > before, TimeSpan.FromSeconds(10));
            starts = view.Started.Count - before;
        }));

        Assert.That(starts, Is.EqualTo(1));
    }

    // A page shown at twice its size lays itself out in half the width.
    [Test]
    public void APageZoomedTwice_LaysOutInHalfTheWidth()
    {
        const string page = "data:text/html,<html style='height:100%;background:%23ff0000'><script>" +
                            "addEventListener('resize',()=>document.documentElement.style.background=" +
                            "innerWidth<=34?'%2300ff00':'%230000ff')</script></html>";
        var (_, _, _, center) = loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            browser.SetZoom(2);
            Pump(() => false, TimeSpan.FromMilliseconds(500));
        }));

        Assert.That(center, Is.EqualTo(new byte[] { 0, 255, 0, 255 }),
            $"green: laid out at half the width (blue: another width) - {string.Join(",", center ?? [])}");
    }

    // The page is searched as a browser searches it: the matches counted, the current one stepped either way and round,
    // capitals told apart when asked; the page going elsewhere, Chromium tells there are none.
    [Test]
    public void APageSearched_CountsItsMatches_AndStepsBetweenThem()
    {
        const string page = "data:text/html,<p>apple Apple</p><p>apple</p><p>pear</p>";
        var found = new List<string>();
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            void Look(string what)
            {
                Pump(() => view.FindFinal, TimeSpan.FromSeconds(10), view);
                Pump(() => false, TimeSpan.FromMilliseconds(100), view);
                found.Add($"{what} {view.FindActive}/{view.FindCount}");
            }

            void Find(string text, bool forward, bool matchCase, bool findNext)
            {
                Pump(() => false, TimeSpan.FromMilliseconds(100), view);
                view.FindFinal = false;
                browser.Find(text, forward, matchCase, findNext);
            }

            Find("apple", true, false, false);
            Look("first");
            Find("apple", true, false, true);
            Look("next");
            Find("apple", true, false, true);
            Find("apple", true, false, true);
            Look("round");
            Find("apple", false, false, true);
            Look("back");
            Find("Apple", true, true, false);
            Look("case");
            Find("plum", true, false, false);
            Look("none");
            Find("apple", true, false, false);
            Look("again");
            var before = view.Completed.Count;
            browser.Navigate("data:text/html,<p>pear</p>");
            Pump(() => view.Completed.Count > before, TimeSpan.FromSeconds(10), view);
            found.Add($"gone {view.FindActive}/{view.FindCount}");
            browser.StopFinding(true);
        }));

        Assert.That(found, Is.EqualTo(new[]
        {
            "first 1/3", "next 2/3", "round 1/3", "back 3/3", "case 1/1", "none 0/0", "again 1/3", "gone 0/0"
        }), string.Join(", ", found));
    }

    // A script answers with its value as JSON, a promise waited for; one that throws says why.
    [Test]
    public void AScript_AnswersWithItsValue_OrWhyItThrew()
    {
        const string page = "data:text/html,<title>Answers</title>";
        string sum = null, title = null, later = null, why = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            sum = Await(browser.EvaluateAsync("1 + 2"));
            title = Await(browser.EvaluateAsync("document.title"));
            later = Await(browser.EvaluateAsync("new Promise(r => setTimeout(() => r({ok: true}), 50))"));
            var thrown = browser.EvaluateAsync("(() => { throw new Error('nope'); })()");
            Pump(() => thrown.IsCompleted, TimeSpan.FromSeconds(5));
            why = thrown.Exception?.InnerException?.Message;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(sum, Is.EqualTo("3"));
            Assert.That(title, Is.EqualTo("\"Answers\""));
            Assert.That(later, Is.EqualTo("{\"ok\":true}"));
            Assert.That(why, Does.Contain("nope"));
        });
    }

    // The page sends with window.adamantium.postMessage and hears what is posted to it.
    [Test]
    public void Messages_GoBothWays()
    {
        const string page = "data:text/html,<title>none</title>";
        string title = null;
        List<string> received = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            Await(browser.EvaluateAsync(
                "window.adamantium.addEventListener('message', e => document.title = 'got ' + e.data.x)"));
            Await(browser.EvaluateAsync("window.adamantium.postMessage({hello: 'world'})"));
            browser.Post("{\"x\":5}");
            Pump(() => view.Title == "got 5" && view.Messages.Count > 0, TimeSpan.FromSeconds(5));
            title = view.Title;
            browser.Navigate("data:text/html,<script>window.adamantium.postMessage('as it loads')</script>");
            Pump(() => view.Messages.Count > 1, TimeSpan.FromSeconds(5));
            received = view.Messages.ToList();
        }));

        Assert.Multiple(() =>
        {
            Assert.That(received, Is.EqualTo(new[] { "{\"hello\":\"world\"}", "\"as it loads\"" }),
                "page to .NET, from a script run and from a page's own as it loads");
            Assert.That(title, Is.EqualTo("got 5"), ".NET to page");
        });
    }

    // A script for new documents runs in each document loaded after it was added, before the page's own scripts and
    // with window.adamantium there, in the order added; the document shown when it came does not run it, and one
    // taken away - even before Chromium answered for it - runs no more.
    [Test]
    public void AScriptForNewDocuments_RunsBeforeThePagesOwn_UntilTakenAway()
    {
        string first = null, afterAdding = null, shownWhenAdded = null, afterRemoving = null;
        var backAtFirst = true;
        List<string> heard = null;
        loop.Run(() =>
        {
            var view = new RecordingBrowserView();
            view.Scripts.Add(new WebDocumentScript(MarkScript("A")));
            var browser = engine.CreateBrowser(false, view);
            try
            {
                browser.Create(MarksPage(1), Width, Height, 1, IntPtr.Zero);
                first = TitleOf(view, 1);
                Pump(() => !view.IsLoading, TimeSpan.FromSeconds(5));
                backAtFirst = view.CanGoBack;

                var b = new WebDocumentScript(MarkScript("B"));
                view.Scripts.Add(b);
                browser.ScriptsForNewDocumentsChanged();
                shownWhenAdded = Await(browser.EvaluateAsync("window.__marks"));
                browser.Navigate(MarksPage(2));
                afterAdding = TitleOf(view, 2);

                var c = new WebDocumentScript(MarkScript("C"));
                view.Scripts.Add(c);
                browser.ScriptsForNewDocumentsChanged();
                view.Scripts.Remove(c);
                view.Scripts.RemoveAt(0);
                browser.ScriptsForNewDocumentsChanged();
                browser.Navigate(MarksPage(3));
                afterRemoving = TitleOf(view, 3);
                heard = [.. view.Started, .. view.Completed];
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("1:A+bridge"), "before the page's own script, window.adamantium there");
            Assert.That(backAtFirst, Is.False, "the first page is the first: nothing to go back to");
            Assert.That(heard, Is.Not.Empty.And.No.EqualTo("about:blank"),
                "nobody hears of the blank page the browser is made with");
            Assert.That(shownWhenAdded, Is.EqualTo("\"A\""), "the document shown when it came does not run it");
            Assert.That(afterAdding, Is.EqualTo("2:AB+bridge"), "in the order added");
            Assert.That(afterRemoving, Is.EqualTo("3:B+bridge"), "taken away, even at once, it runs no more");
        });
    }

    // The DevTools front end may turn the page off, which drops its scripts for new documents: gone, the page's
    // scripts run again in the next document - once each.
    [Test]
    public void ScriptsForNewDocuments_OutliveAFrontEndTurningThePageOff()
    {
        string title = null;
        loop.Run(() =>
        {
            var view = new RecordingBrowserView();
            view.Scripts.Add(new WebDocumentScript(MarkScript("A")));
            var browser = engine.CreateBrowser(false, view);
            try
            {
                browser.Create(MarksPage(1), Width, Height, 1, IntPtr.Zero);
                TitleOf(view, 1);
                var address = browser.OpenDevTools();
                var socketAddress = new Uri("ws://" + address[(address.IndexOf("ws=", StringComparison.Ordinal) + 3)..]);
                using var frontEnd = new System.Net.WebSockets.ClientWebSocket();
                frontEnd.Options.SetRequestHeader("Origin", "devtools://devtools");
                var connected = frontEnd.ConnectAsync(socketAddress, CancellationToken.None);
                Pump(() => connected.IsCompleted, TimeSpan.FromSeconds(5));
                frontEnd.SendAsync("{\"id\":1,\"method\":\"Page.disable\",\"params\":{}}"u8.ToArray(),
                    System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                Receive(frontEnd, reply => reply.Contains("\"id\":1"));
                browser.CloseDevTools();
                browser.Navigate(MarksPage(2));
                title = TitleOf(view, 2);
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.That(title, Is.EqualTo("2:A+bridge"));
    }

    // A page at the origin an object was added for calls it: a value, an awaited one, the object's failure, another
    // object it answers with, the page's own function called back, a value of several fields - and a method there is
    // none of, or a public member outside the interface, is refused.
    [Test]
    public void AnObjectOfTheApplications_IsCalledByThePage()
    {
        using var site = new LocalSite("<title>site</title><body></body>");
        var answers = new Dictionary<string, string>();
        loop.Run(() =>
        {
            var bridge = new WebViewBridge();
            bridge.AddHostObject("calc", new Calculator(), site.Origin);
            var view = new RecordingBrowserView { Bridge = bridge };
            var browser = engine.CreateBrowser(false, view);
            try
            {
                browser.Create(site.Origin + "/", Width, Height, 1, IntPtr.Zero);
                Pump(() => view.Title == "site" && !view.IsLoading, TimeSpan.FromSeconds(30));
                const string calc = "adamantium.hostObjects.calc";
                const string refused = ".then(v => 'resolved', e => 'refused: ' + e.message)";
                answers["add"] = Await(browser.EvaluateAsync(calc + ".add(2, 3)"));
                answers["greet"] = Await(browser.EvaluateAsync(calc + ".greetAsync('web')"));
                answers["fail"] = Await(browser.EvaluateAsync(calc + ".fail()" + refused));
                answers["counter"] = Await(browser.EvaluateAsync(
                    "(async () => { const c = await " + calc + ".counter(); await c.increment(); return await c.increment(); })()"));
                answers["callBack"] = Await(browser.EvaluateAsync(calc + ".callBackAsync((a, b) => a + b)"));
                answers["mirror"] = Await(browser.EvaluateAsync(calc + ".mirror({x: 1, y: 2})"));
                answers["nothing"] = Await(browser.EvaluateAsync(calc + ".nothing()" + refused));
                answers["secret"] = Await(browser.EvaluateAsync(calc + ".secret()" + refused));
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(answers["add"], Is.EqualTo("5"));
            Assert.That(answers["greet"], Is.EqualTo("\"hello web\""), "an awaited method is awaited");
            Assert.That(answers["fail"], Is.EqualTo("\"refused: broken\""), "what it throws refuses the promise");
            Assert.That(answers["counter"], Is.EqualTo("2"), "an object answered with is called in turn");
            Assert.That(answers["callBack"], Is.EqualTo("\"42\""), "the page's function called back");
            Assert.That(answers["mirror"], Is.EqualTo("{\"x\":2,\"y\":1}"), "a value of several fields, both ways");
            Assert.That(answers["nothing"], Does.StartWith("\"refused: There is no method 'nothing'"));
            Assert.That(answers["secret"], Does.StartWith("\"refused: There is no method 'secret'"),
                "a public member outside the interface is no page's");
        });
    }

    // An object reaches no page it was not let in for: another origin, a frame of the page's own origin, a page after
    // it is taken away - all refused alike, as if there were none.
    [Test]
    public void AnObjectOfTheApplications_ReachesOnlyTheMainFrameAtItsOrigins_UntilTakenAway()
    {
        using var site = new LocalSite("<title>site</title><body><iframe srcdoc='<p>frame</p>'></iframe></body>");
        using var other = new LocalSite("<title>other</title><body></body>");
        string ownMain = null, ownFrame = null, otherSite = null, afterRemoving = null;
        loop.Run(() =>
        {
            var bridge = new WebViewBridge();
            bridge.AddHostObject("calc", new Calculator(), site.Origin);
            var view = new RecordingBrowserView { Bridge = bridge };
            var browser = engine.CreateBrowser(false, view);
            try
            {
                const string call = "adamantium.hostObjects.calc.add(1, 1).then(v => 'got ' + v, e => e.message)";
                browser.Create(site.Origin + "/", Width, Height, 1, IntPtr.Zero);
                Pump(() => view.Title == "site" && !view.IsLoading, TimeSpan.FromSeconds(30));
                ownMain = Await(browser.EvaluateAsync(call));
                ownFrame = Await(browser.EvaluateAsync("frames[0]." + call));

                browser.Navigate(other.Origin + "/");
                Pump(() => view.Title == "other" && !view.IsLoading, TimeSpan.FromSeconds(30));
                otherSite = Await(browser.EvaluateAsync(call));

                browser.Navigate(site.Origin + "/again");
                Pump(() => view.Title == "site" && !view.IsLoading, TimeSpan.FromSeconds(30));
                bridge.RemoveHostObject("calc");
                afterRemoving = Await(browser.EvaluateAsync(call));
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(ownMain, Is.EqualTo("\"got 2\""));
            Assert.That(ownFrame, Is.EqualTo("\"There is no object 'calc' here.\""), "a frame is not the main frame");
            Assert.That(otherSite, Is.EqualTo("\"There is no object 'calc' here.\""), "another origin");
            Assert.That(afterRemoving, Is.EqualTo("\"There is no object 'calc' here.\""), "taken away");
        });
    }

    // An object taken away while a call of it still runs: the object the call answers with is not given - the page
    // reaches nothing of what was taken away.
    [Test]
    public void AnObjectTakenAwayMidCall_GivesNothingItsCallAnswers()
    {
        using var site = new LocalSite("<title>site</title><body></body>");
        string answer = null;
        loop.Run(() =>
        {
            var bridge = new WebViewBridge();
            bridge.AddHostObject("calc", new Calculator(), site.Origin);
            var view = new RecordingBrowserView { Bridge = bridge };
            var browser = engine.CreateBrowser(false, view);
            try
            {
                browser.Create(site.Origin + "/", Width, Height, 1, IntPtr.Zero);
                Pump(() => view.Title == "site" && !view.IsLoading, TimeSpan.FromSeconds(30));
                var later = browser.EvaluateAsync(
                    "adamantium.hostObjects.calc.counterLaterAsync().then(c => c.increment()).then(v => 'got ' + v, e => e.message)");
                Pump(() => false, TimeSpan.FromMilliseconds(100));
                bridge.RemoveHostObject("calc");
                answer = Await(later);
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.That(answer, Is.EqualTo("\"The object is gone.\""));
    }

    // Asked to be blank before it could go anywhere: the blank page it was made with is the page asked for - told as
    // it, nothing behind it - and what comes next is heard as ever.
    [Test]
    public void ABlankPageAskedForAtOnce_IsThePage_AndThePageIsHeardAfter()
    {
        string address = null, titleAfter = null;
        var completedBlank = false;
        var backOnBlank = true;
        loop.Run(() =>
        {
            var view = new RecordingBrowserView();
            view.Scripts.Add(new WebDocumentScript(MarkScript("A")));
            var browser = engine.CreateBrowser(false, view);
            try
            {
                browser.Create(MarksPage(1), Width, Height, 1, IntPtr.Zero);
                browser.Navigate("about:blank");
                Pump(() => view.Completed.Contains("about:blank"), TimeSpan.FromSeconds(10));
                address = view.Address;
                completedBlank = view.Completed.Contains("about:blank");
                backOnBlank = view.CanGoBack;
                browser.Navigate(MarksPage(2));
                titleAfter = TitleOf(view, 2);
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(address, Is.EqualTo("about:blank"));
            Assert.That(completedBlank, Is.True, "completed, as a page asked for");
            Assert.That(backOnBlank, Is.False);
            Assert.That(titleAfter, Is.EqualTo("2:A+bridge"), "the next page heard, its scripts run");
        });
    }

    // A site of the application's: its page loads at app://, fetches its files, at an origin of its own; a file or a
    // site there is none of, or a path out of its folder, is not found; a site built into an assembly loads alike.
    [Test]
    public void AnAppSitesPage_LoadsItsFiles_AndWhatIsNotThereIsNotFound()
    {
        var parent = Path.Combine(Path.GetTempPath(), "AdamantiumAppSite", Guid.NewGuid().ToString("N"));
        var folder = Path.Combine(parent, "site");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "index.html"),
            "<title>app</title><script>fetch('data.json').then(r => r.json())" +
            ".then(d => document.title = 'app:' + d.n + ':' + location.origin)</script>");
        File.WriteAllText(Path.Combine(folder, "data.json"), "{\"n\":42}");
        File.WriteAllText(Path.Combine(parent, "secret.txt"), "secret");
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "sub", "index.html"), "<title>sub</title><script src='s.js'></script>");
        File.WriteAllText(Path.Combine(folder, "sub", "s.js"), "document.title = 'sub:' + location.pathname");
        engine.AddSite("test", new WebSiteFolder(folder));
        engine.AddSite("built", new WebSiteResources(typeof(WebViewFrameTests).Assembly, "Site"));
        string title = null, missing = null, outside = null, noSite = null, builtIn = null, folderTitle = null;
        try
        {
            loop.Run(() =>
            {
                var view = new RecordingBrowserView();
                var browser = engine.CreateBrowser(false, view);
                try
                {
                    browser.Create("app://test/", Width, Height, 1, IntPtr.Zero);
                    Pump(() => view.Title?.StartsWith("app:", StringComparison.Ordinal) == true, TimeSpan.FromSeconds(30));
                    title = view.Title;
                    missing = Await(browser.EvaluateAsync("fetch('app://test/nothing.txt').then(r => r.status)"));
                    outside = Await(browser.EvaluateAsync("fetch('app://test/..%2fsecret.txt').then(r => r.status)"));
                    noSite = Await(browser.EvaluateAsync("fetch('app://nowhere/index.html').then(r => r.status, e => 'refused')"));
                    browser.Navigate("app://built/");
                    Pump(() => view.Title == "built in", TimeSpan.FromSeconds(10));
                    builtIn = view.Title;
                    browser.Navigate("app://test/sub");
                    Pump(() => view.Title?.StartsWith("sub:", StringComparison.Ordinal) == true, TimeSpan.FromSeconds(10));
                    folderTitle = view.Title;
                }
                finally
                {
                    browser.Dispose();
                    Pump(() => false, TimeSpan.FromMilliseconds(300));
                }
            });
        }
        finally
        {
            engine.RemoveSite("test");
            engine.RemoveSite("built");
            Directory.Delete(parent, true);
        }

        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo("app:42:app://test"), "loaded, its file fetched, at its own origin");
            Assert.That(missing, Is.EqualTo("404"));
            Assert.That(outside, Is.EqualTo("404"), "a path out of the folder");
            Assert.That(noSite, Is.EqualTo("404").Or.EqualTo("\"refused\""), "a site there is none of");
            Assert.That(builtIn, Is.EqualTo("built in"), "a site built into an assembly");
            Assert.That(folderTitle, Is.EqualTo("sub:/sub/"),
                "a folder without its last slash is sent to the one with it, where its own addresses resolve");
        });
    }

    // A site out there cannot send its page to the application's own sites, nor reach their files.
    [Test]
    public void ASiteOutThere_CannotReachTheApplicationsSites()
    {
        using var site = new LocalSite("<title>site</title><body></body>");
        engine.AddSite("built", new WebSiteResources(typeof(WebViewFrameTests).Assembly, "Site"));
        string titleAfter = null, fetched = null;
        try
        {
            loop.Run(() =>
            {
                var view = new RecordingBrowserView();
                var browser = engine.CreateBrowser(false, view);
                try
                {
                    browser.Create(site.Origin + "/", Width, Height, 1, IntPtr.Zero);
                    Pump(() => view.Title == "site" && !view.IsLoading, TimeSpan.FromSeconds(30));
                    fetched = Await(browser.EvaluateAsync("fetch('app://built/').then(r => 'came', () => 'refused')"));
                    Await(browser.EvaluateAsync("location.href = 'app://built/'; 1"));
                    Pump(() => view.Title == "built in", TimeSpan.FromSeconds(2));
                    titleAfter = view.Title;
                }
                finally
                {
                    browser.Dispose();
                    Pump(() => false, TimeSpan.FromMilliseconds(300));
                }
            });
        }
        finally
        {
            engine.RemoveSite("built");
        }

        Assert.Multiple(() =>
        {
            Assert.That(titleAfter, Is.EqualTo("site"), "the page is not sent there");
            Assert.That(fetched, Is.EqualTo("\"refused\""), "nor are its files fetched");
        });
    }

    // The point of a site of one's own: its origin can be let in, and its page calls the objects let in for it.
    [Test]
    public void AnAppSitesPage_CallsAnObjectLetInForItsOrigin()
    {
        engine.AddSite("built", new WebSiteResources(typeof(WebViewFrameTests).Assembly, "Site"));
        string answer = null;
        try
        {
            loop.Run(() =>
            {
                var bridge = new WebViewBridge();
                bridge.AddHostObject("calc", new Calculator(), "app://built");
                var view = new RecordingBrowserView { Bridge = bridge };
                var browser = engine.CreateBrowser(false, view);
                try
                {
                    browser.Create("app://built/", Width, Height, 1, IntPtr.Zero);
                    Pump(() => view.Title == "built in" && !view.IsLoading, TimeSpan.FromSeconds(30));
                    answer = Await(browser.EvaluateAsync(
                        "adamantium.hostObjects.calc.add(2, 3).then(v => v, e => e.message + ' at ' + location.origin)"));
                }
                finally
                {
                    browser.Dispose();
                    Pump(() => false, TimeSpan.FromMilliseconds(300));
                }
            });
        }
        finally
        {
            engine.RemoveSite("built");
        }

        Assert.That(answer, Is.EqualTo("5"));
    }

    // A request the page makes is asked of the bridge listening: answered by it, refused, sent elsewhere - the page's
    // document too - each told for what it is.
    [Test]
    public void APagesRequests_AreAnsweredRefusedOrSentElsewhere_ByTheBridgeListening()
    {
        using var site = new LocalSite("<title>site</title><body></body>");
        var kinds = new List<(string Address, WebResourceKind Kind)>();
        string answered = null, refused = null, redirected = null, madeTitle = null, written = null, thrown = null;
        loop.Run(() =>
        {
            var bridge = new WebViewBridge();
            bridge.ResourceRequested += (_, asked) =>
            {
                kinds.Add((asked.Address, asked.Kind));
                if (asked.Address.EndsWith("/answered.json", StringComparison.Ordinal))
                {
                    asked.Respond(new MemoryStream("{\"n\":7}"u8.ToArray()), "application/json");
                }
                else if (asked.Address.EndsWith("/refused.txt", StringComparison.Ordinal))
                {
                    asked.Refuse();
                }
                else if (asked.Address.EndsWith("/old", StringComparison.Ordinal))
                {
                    asked.Redirect(site.Origin + "/new");
                }
                else if (asked.Address.EndsWith("/made", StringComparison.Ordinal))
                {
                    asked.Respond(new MemoryStream("<title>made</title>"u8.ToArray()), "text/html");
                }
                else if (asked.Address.EndsWith("/written.json", StringComparison.Ordinal))
                {
                    // Written and not rewound: still read from its start.
                    var written = new MemoryStream();
                    written.Write("{\"n\":8}"u8);
                    asked.Respond(written, "application/json");
                }
                else if (asked.Address.EndsWith("/thrown.txt", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("a handler that broke");
                }
            };
            var view = new RecordingBrowserView { Bridge = bridge };
            var browser = engine.CreateBrowser(false, view);
            browser.InterceptsResources = true;
            try
            {
                browser.Create(site.Origin + "/", Width, Height, 1, IntPtr.Zero);
                Pump(() => view.Title == "site" && !view.IsLoading, TimeSpan.FromSeconds(30));
                answered = Await(browser.EvaluateAsync("fetch('/answered.json').then(r => r.json()).then(d => d.n)"));
                refused = Await(browser.EvaluateAsync("fetch('/refused.txt').then(() => 'came', () => 'refused')"));
                redirected = Await(browser.EvaluateAsync("fetch('/old').then(r => r.url)"));
                written = Await(browser.EvaluateAsync("fetch('/written.json').then(r => r.json()).then(d => d.n)"));
                thrown = Await(browser.EvaluateAsync("fetch('/thrown.txt').then(() => 'came', () => 'refused')"));
                browser.Navigate(site.Origin + "/made");
                Pump(() => view.Title == "made", TimeSpan.FromSeconds(10));
                madeTitle = view.Title;
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(answered, Is.EqualTo("7"), "answered by the bridge");
            Assert.That(refused, Is.EqualTo("\"refused\""));
            Assert.That(redirected, Is.EqualTo("\"" + site.Origin + "/new\""), "sent elsewhere");
            Assert.That(madeTitle, Is.EqualTo("made"), "the page's document answered by the bridge");
            Assert.That(written, Is.EqualTo("8"), "an answer written and not rewound is read from its start");
            Assert.That(thrown, Is.EqualTo("\"refused\""), "a handler that throws refuses the request");
            Assert.That(kinds, Does.Contain((site.Origin + "/", WebResourceKind.Document)));
            Assert.That(kinds, Does.Contain((site.Origin + "/answered.json", WebResourceKind.Fetch)));
        });
    }

    // Nobody listening, no request is asked: the page loads as ever.
    [Test]
    public void APagesRequests_AreAskedOfNobody_WhenNobodyListens()
    {
        using var site = new LocalSite("<title>site</title><body></body>");
        var asked = 0;
        string title = null;
        loop.Run(() =>
        {
            var bridge = new WebViewBridge();
            void Count(object sender, WebResourceRequestedEventArgs e) => asked++;
            bridge.ResourceRequested += Count;
            bridge.ResourceRequested -= Count;
            var view = new RecordingBrowserView { Bridge = bridge };
            var browser = engine.CreateBrowser(false, view);
            try
            {
                browser.Create(site.Origin + "/", Width, Height, 1, IntPtr.Zero);
                Pump(() => view.Title == "site" && !view.IsLoading, TimeSpan.FromSeconds(30));
                title = view.Title;
            }
            finally
            {
                browser.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo("site"));
            Assert.That(asked, Is.Zero);
        });
    }

    private static string MarkScript(string mark) =>
        "window.__marks = (window.__marks || '') + '" + mark + "'; window.__bridge = !!window.adamantium;";

    private static string MarksPage(int load) =>
        "data:text/html,<script>document.title = '" + load + ":' + (window.__marks || '') + " +
        "(window.__bridge ? '+bridge' : '')</script>";

    private static string TitleOf(RecordingBrowserView view, int load)
    {
        Pump(() => view.Title?.StartsWith(load + ":", StringComparison.Ordinal) == true, TimeSpan.FromSeconds(30));
        return view.Title;
    }

    // The DevTools front end talks to the page through the page's socket: its calls answered under its own ids while
    // the page's own calls go on beside them; a new front end takes the old one's place; closed, the page's messages
    // still come.
    [Test]
    public void TheDevToolsFrontEnd_TalksToThePage_BesideThePagesOwnCalls()
    {
        const string page = "data:text/html,<title>Inspected</title>";
        string answer = null, ours = null, address = null, secondAnswer = null;
        var firstLetGo = false;
        List<string> messagesAfter = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            address = browser.OpenDevTools();
            var socketAddress = new Uri("ws://" + address[(address.IndexOf("ws=", StringComparison.Ordinal) + 3)..]);
            using var frontEnd = new System.Net.WebSockets.ClientWebSocket();
            frontEnd.Options.SetRequestHeader("Origin", "devtools://devtools");
            var connected = frontEnd.ConnectAsync(socketAddress, CancellationToken.None);
            Pump(() => connected.IsCompleted, TimeSpan.FromSeconds(5));

            var call = "{\"id\":1,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"6*7\",\"returnByValue\":true}}";
            var sent = frontEnd.SendAsync(System.Text.Encoding.UTF8.GetBytes(call),
                System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            ours = Await(browser.EvaluateAsync("1 + 1"));
            answer = Receive(frontEnd, reply => reply.Contains("\"id\":1"));

            Pump(() => sent.IsCompleted, TimeSpan.FromSeconds(1));
            using var second = new System.Net.WebSockets.ClientWebSocket();
            second.Options.SetRequestHeader("Origin", "devtools://devtools");
            var secondConnect = second.ConnectAsync(socketAddress, CancellationToken.None);
            Pump(() => secondConnect.IsCompleted, TimeSpan.FromSeconds(5));
            var secondCall = "{\"id\":1,\"method\":\"Runtime.evaluate\",\"params\":{\"expression\":\"5*5\",\"returnByValue\":true}}";
            second.SendAsync(System.Text.Encoding.UTF8.GetBytes(secondCall),
                System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            secondAnswer = Receive(second, reply => reply.Contains("\"id\":1"));
            firstLetGo = Receive(frontEnd, _ => false) == null && frontEnd.State != System.Net.WebSockets.WebSocketState.Open;

            browser.CloseDevTools();
            Await(browser.EvaluateAsync("window.adamantium.postMessage('after')"));
            Pump(() => view.Messages.Count > 0, TimeSpan.FromSeconds(5));
            messagesAfter = view.Messages.ToList();
        }));

        Assert.Multiple(() =>
        {
            Assert.That(address, Does.StartWith("devtools://devtools/bundled/devtools_app.html?ws=127.0.0.1:"));
            Assert.That(answer, Does.Contain("\"id\":1").And.Contain("42"), "the front end's call, under its own id");
            Assert.That(ours, Is.EqualTo("2"), "the page's own call beside it");
            Assert.That(secondAnswer, Does.Contain("\"id\":1").And.Contain("25"),
                "a new front end takes the old one's place, its ids its own");
            Assert.That(firstLetGo, Is.True, "the old front end is let go");
            Assert.That(messagesAfter, Is.EqualTo(new[] { "\"after\"" }), "the page's messages after the DevTools close");
        });
    }

    // A right press asks for the page's menu: Chromium's rows come, and a row chosen runs in the page.
    [Test]
    public void ARightPress_BringsThePagesMenu_AndARowRunsInThePage()
    {
        const string page = "data:text/html,<body style='margin:0'>" +
                            "<input id='field' value='abc' style='width:100%;height:100vh'></body>";
        WebViewContextMenu menu = null;
        string selection = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            Click(browser, Width / 2, Height / 2);
            browser.SendMouseClick(Width / 2, Height / 2, InputModifiers.RightMouseButton, CefMouseButtonType.Right, false, 1);
            browser.SendMouseClick(Width / 2, Height / 2, InputModifiers.None, CefMouseButtonType.Right, true, 1);
            Pump(() => view.Menus.Count > 0, TimeSpan.FromSeconds(5));
            menu = view.Menus.FirstOrDefault();
            menu?.Choose((int)CefMenuId.SelectAll);
            Pump(() => false, TimeSpan.FromMilliseconds(500));
            selection = Await(browser.EvaluateAsync("field.selectionStart + ',' + field.selectionEnd"));
        }));

        Assert.Multiple(() =>
        {
            Assert.That(menu, Is.Not.Null, "the menu was asked for");
            Assert.That(menu?.Items.Select(item => item.Command), Does.Contain((int)CefMenuId.SelectAll));
            Assert.That(menu?.X, Is.EqualTo(Width / 2).Within(1));
            Assert.That(menu?.IsAnswered, Is.True);
            Assert.That(selection, Is.EqualTo("\"0,3\""), "Select all chosen selects the field's text");
        });
    }

    // A select's list is dropped apart from the page - placed, drawn as frames of its own - and a press on it chooses.
    [TestCase(WebViewFramePath.PixelBuffer)]
    [TestCase(WebViewFramePath.SharedTexture)]
    public void ASelectsList_IsDroppedApart_AndAPressOnItChooses(WebViewFramePath path)
    {
        // Small enough for its three rows to fit the view under it, which is small.
        const string page = "data:text/html,<body style='margin:0'><select id='pick' style='font-size:7px'>" +
                            "<option>a</option><option>b</option><option>c</option></select></body>";
        Rect? dropped = null;
        SharedSurfaceDescriptor frames = null;
        string chosen = null;
        var closed = false;
        var (taken, _, _, _) = loop.Run(() => ShowPage(path, page, (browser, view) =>
        {
            Click(browser, 5, 5);
            Pump(() => view.Popup != null && view.PopupShown?.Width > 0, TimeSpan.FromSeconds(5));
            dropped = view.Popup;
            frames = view.PopupShown;
            if (dropped is not { } list)
            {
                return;
            }

            // Onto a row below the first, as a hand would come to it: the list lights the row under the pointer. How tall
            // its rows are is Chromium's.
            var x = list.X + 5;
            var y = list.Y + list.Height - 3;
            browser.SendMouseMove(x, list.Y + 2, InputModifiers.None, false);
            browser.SendMouseMove(x, y, InputModifiers.None, false);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            browser.SendMouseClick(x, y, InputModifiers.LeftMouseButton, CefMouseButtonType.Left, false, 1);
            Pump(() => false, TimeSpan.FromMilliseconds(100));
            browser.SendMouseClick(x, y, InputModifiers.None, CefMouseButtonType.Left, true, 1);
            Pump(() => view.Popup == null, TimeSpan.FromSeconds(5));
            closed = view.Popup == null;
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            chosen = Await(browser.EvaluateAsync("pick.value"));
        }));
        if (taken != path)
        {
            Assert.Ignore($"This device takes the {taken} path, not the {path} one.");
        }

        Assert.Multiple(() =>
        {
            Assert.That(dropped, Is.Not.Null, "the list was dropped");
            Assert.That(dropped?.Height, Is.GreaterThan(0), "it has a size");
            Assert.That(frames?.Width, Is.GreaterThan(0u), "its frames are its own");
            Assert.That(closed, Is.True, "a row pressed puts it away");
            Assert.That(chosen, Is.Not.EqualTo("\"a\"").And.Not.Null, "a row below the first chosen");
        });
    }

    // A script's dialogs are asked of the view, and its answers go back to the script: confirm's yes, prompt's text,
    // alert's OK.
    [Test]
    public void AScriptsDialogs_AreAskedOfTheView_AndAnsweredBack()
    {
        const string page = "data:text/html,<title>asks</title>";
        WebViewScriptDialogRequest confirm = null, prompt = null, alert = null;
        string confirmed = null, typed = null, alerted = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            var asking = browser.EvaluateAsync("confirm('Sure?')");
            Pump(() => view.Dialogs.Count > 0, TimeSpan.FromSeconds(5));
            confirm = view.Dialogs.LastOrDefault();
            confirm?.Answer(true, null);
            confirmed = Await(asking);

            asking = browser.EvaluateAsync("prompt('Name?', 'x')");
            Pump(() => view.Dialogs.Count > 1, TimeSpan.FromSeconds(5));
            prompt = view.Dialogs.LastOrDefault();
            prompt?.Answer(true, "Anna");
            typed = Await(asking);

            asking = browser.EvaluateAsync("alert('Hi'), 7");
            Pump(() => view.Dialogs.Count > 2, TimeSpan.FromSeconds(5));
            alert = view.Dialogs.LastOrDefault();
            alert?.Answer(true, null);
            alerted = Await(asking);
        }));

        Assert.Multiple(() =>
        {
            Assert.That((confirm?.Kind, confirm?.Message), Is.EqualTo((WebViewScriptDialogKind.Confirm, "Sure?")));
            Assert.That(confirmed, Is.EqualTo("true"));
            Assert.That((prompt?.Kind, prompt?.DefaultText), Is.EqualTo((WebViewScriptDialogKind.Prompt, "x")));
            Assert.That(typed, Is.EqualTo("\"Anna\""));
            Assert.That(alert?.Kind, Is.EqualTo(WebViewScriptDialogKind.Alert));
            Assert.That(alerted, Is.EqualTo("7"));
        });
    }

    // A page that asks before it is left asks the view; answered "stay", it stays - and closed, it closes, asking nobody.
    [Test]
    public void APageAskingBeforeItIsLeft_StaysWhenTheAnswerIsStay_AndClosesWhenClosed()
    {
        const string page = "data:text/html,<body style='margin:0;height:100vh' " +
                            "onload=\"addEventListener('beforeunload', e => { e.preventDefault(); e.returnValue = ''; })\">" +
                            "stay</body>";
        WebViewScriptDialogRequest asked = null;
        string title = null;
        RecordingBrowserView shown = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            shown = view;
            // A page asks only after the user has done something on it.
            Click(browser, Width / 2, Height / 2);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            browser.Navigate("data:text/html,<title>gone</title>");
            Pump(() => view.Dialogs.Count > 0, TimeSpan.FromSeconds(5));
            asked = view.Dialogs.LastOrDefault();
            asked?.Answer(false, null);
            Pump(() => false, TimeSpan.FromSeconds(1));
            title = view.Title;
        }));
        var left = loop.Run(() =>
        {
            Pump(() => engine.BrowserCount == 0, TimeSpan.FromSeconds(10));
            return engine.BrowserCount;
        });

        Assert.Multiple(() =>
        {
            Assert.That(asked?.Kind, Is.EqualTo(WebViewScriptDialogKind.LeavePage));
            Assert.That(title, Is.Not.EqualTo("gone"), "the page stayed");
            Assert.That(left, Is.Zero, "the page closed");
            Assert.That(shown?.Dialogs.Count, Is.EqualTo(1), "closing, it asked nobody");
        });
    }

    // A window the page opens on a click comes to the view as a new page: Chromium's own, which another view takes up -
    // drawn there, its window.opener still the page that opened it.
    [Test]
    public void AWindowOpenedOnAClick_IsANewPage_WhoseOpenerIsThePage()
    {
        const string page = "data:text/html,<body style='margin:0;height:100vh' onclick=\"w=window.open('','_blank');" +
                            "w.document.write('<title>opener:'+(w.opener===window)+'</title><p>new</p>')\"></body>";
        WebNewWindowKind? kind = null;
        string address = null, title = null, message = null;
        var frames = 0;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            ClickTheMiddle(browser);
            Pump(() => view.NewPages.Count > 0, TimeSpan.FromSeconds(5));
            if (view.NewPages.Count == 0)
            {
                return;
            }

            (var opened, kind) = view.NewPages[0];
            address = opened.Address;
            var shown = new RecordingBrowserView();
            var popup = opened.Take(out var waited);
            popup.Adopt(shown, waited);
            popup.SetHidden(false);
            var consumer = new FrameConsumer(devices);
            try
            {
                Pump(() => shown.Title?.StartsWith("opener") == true && shown.Frames > 0, TimeSpan.FromSeconds(10), shown,
                    consumer);
                title = shown.Title;
                frames = shown.Frames;
                Await(popup.EvaluateAsync("window.adamantium.postMessage({ from: 'popup' }), 1"));
                Pump(() => shown.Messages.Count > 0, TimeSpan.FromSeconds(5), shown, consumer);
                message = shown.Messages.FirstOrDefault();
            }
            finally
            {
                popup.Dispose();
                Pump(() => false, TimeSpan.FromMilliseconds(300));
                consumer.Dispose();
            }
        }));

        Assert.Multiple(() =>
        {
            Assert.That(kind, Is.EqualTo(WebNewWindowKind.ForegroundTab));
            Assert.That(address, Is.EqualTo("about:blank"));
            Assert.That(title, Is.EqualTo("opener:true"), "the opener reaches the page it opened");
            Assert.That(frames, Is.GreaterThan(0), "drawn for the view that took it up");
            Assert.That(message, Is.EqualTo("{\"from\":\"popup\"}"), "and its messages come to that view");
        });
    }

    // A page the view model lets go of while it is asked - shown and dropped at once - is refused, its browser never
    // made: nothing is drawn for nobody, and the page that asked stays.
    [Test]
    public void ANewPageLetGoOfAtOnce_IsRefused()
    {
        const string page = "data:text/html,<title>stays</title><body style='margin:0;height:100vh' " +
                            "onclick=\"window.open('about:blank%23gone','_blank')\"></body>";
        var asked = 0;
        string title = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            view.DropsNewPages = true;
            ClickTheMiddle(browser);
            Pump(() => view.NewPages.Count > 0, TimeSpan.FromSeconds(5));
            Pump(() => false, TimeSpan.FromMilliseconds(500));
            asked = view.NewPages.Count;
            title = view.Title;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(title, Is.EqualTo("stays"));
        });
    }

    // A page's source is told as its own address: "view-source:" and the page's, not the page's alone - Enter in an
    // address box showing it would leave the source for the page.
    [Test]
    public void APagesSource_IsToldAtItsOwnAddress()
    {
        const string page = "data:text/html,<title>src</title>";
        string address = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            browser.Navigate("view-source:" + page);
            Pump(() => view.Address?.StartsWith("view-source:") == true, TimeSpan.FromSeconds(5));
            address = view.Address;
        }));

        Assert.That(address, Is.EqualTo("view-source:" + page));
    }

    // A window a script opens on its own, the user doing nothing, is refused before the view hears of it.
    [Test]
    public void AWindowAScriptOpensOnItsOwn_IsRefused()
    {
        const string page = "data:text/html,<title>here</title><body onload=\"window.open('about:blank%23x','_blank')\"></body>";
        var asked = -1;
        string address = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            Pump(() => false, TimeSpan.FromSeconds(1));
            asked = view.NewPages.Count;
            address = view.Address;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.Zero);
            Assert.That(address, Does.StartWith("data:"), "and the page stays where it is");
        });
    }

    // A window nobody shows apart: the page goes there itself, as before pages were asked for.
    [Test]
    public void ANewPageNobodyShows_OpensInThePageItself()
    {
        const string page = "data:text/html,<body style='margin:0;height:100vh' " +
                            "onclick=\"window.open('about:blank%23there','_blank')\"></body>";
        string address = null;
        var asked = 0;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            view.ShowsNewPages = false;
            ClickTheMiddle(browser);
            Pump(() => view.Address?.EndsWith("#there") == true, TimeSpan.FromSeconds(5));
            asked = view.NewPages.Count;
            address = view.Address;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.EqualTo(1));
            Assert.That(address, Is.EqualTo("about:blank#there"));
        });
    }

    // A link opened with Ctrl comes as a page behind the open one, and the page that had the link stays.
    [Test]
    public void ALinkOpenedWithCtrl_IsANewPageBehind_AndThePageStays()
    {
        const string page = "data:text/html,<title>links</title><a href='about:blank%23ctrl' " +
                            "style='display:block;height:100vh'>link</a>";
        WebNewWindowKind? kind = null;
        string address = null, stayedAt = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            browser.SetFocus(true);
            browser.SendMouseMove(Width / 2, Height / 2, InputModifiers.LeftControl, false);
            browser.SendMouseClick(Width / 2, Height / 2, InputModifiers.LeftControl | InputModifiers.LeftMouseButton,
                CefMouseButtonType.Left, false, 1);
            browser.SendMouseClick(Width / 2, Height / 2, InputModifiers.LeftControl, CefMouseButtonType.Left, true, 1);
            Pump(() => view.NewPages.Count > 0, TimeSpan.FromSeconds(5));
            if (view.NewPages.Count > 0)
            {
                (var opened, kind) = view.NewPages[0];
                address = opened.Address;
                opened.Dispose();
            }

            Pump(() => false, TimeSpan.FromMilliseconds(500));
            stayedAt = view.Title;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(kind, Is.EqualTo(WebNewWindowKind.BackgroundTab));
            Assert.That(address, Is.EqualTo("about:blank#ctrl"));
            Assert.That(stayedAt, Is.EqualTo("links"));
        });
    }

    // A file field asks the view - what it takes, as filters with their extensions - and the file chosen reaches the
    // page.
    [Test]
    public void AFileField_AsksTheView_AndTheFileChosenReachesThePage()
    {
        const string page = "data:text/html,<input type=file accept='image/*,.txt' " +
                            "onchange=\"document.title='got:'+this.files[0].name\" style='display:block;width:100%;height:100vh'>";
        var file = Path.Combine(profile, "pick.txt");
        Directory.CreateDirectory(profile);
        File.WriteAllText(file, "picked");
        WebFileDialogMode? mode = null;
        IReadOnlyList<WebFileFilter> filters = null;
        string title = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            ClickTheMiddle(browser);
            Pump(() => view.FileDialogs.Count > 0, TimeSpan.FromSeconds(5));
            if (view.FileDialogs.Count == 0)
            {
                return;
            }

            var asked = view.FileDialogs[0];
            mode = asked.Mode;
            filters = asked.Filters;
            asked.Choose([file]);
            Pump(() => view.Title?.StartsWith("got:") == true, TimeSpan.FromSeconds(5));
            title = view.Title;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(mode, Is.EqualTo(WebFileDialogMode.Open));
            Assert.That(filters?.SelectMany(filter => filter.Extensions), Does.Contain("txt").And.Contain("png"));
            Assert.That(title, Is.EqualTo("got:pick.txt"));
        });
    }

    // Answered through the platform's own dialog: it is offered the kinds the field takes, and the file it gives reaches
    // the page.
    [Test]
    public void AFileField_AnsweredFromThePlatformsDialog_GetsTheFileChosenThere()
    {
        const string page = "data:text/html,<input type=file accept='.txt' " +
                            "onchange=\"document.title='got:'+this.files[0].name\" style='display:block;width:100%;height:100vh'>";
        Directory.CreateDirectory(profile);
        var file = Path.Combine(profile, "platform.txt");
        File.WriteAllText(file, "chosen");
        var platform = new ChoosingFileDialog(file);
        var before = Adamantium.UI.Core.FileDialog.Platform;
        string title = null;
        try
        {
            Adamantium.UI.Core.FileDialog.Platform = platform;
            loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
            {
                ClickTheMiddle(browser);
                Pump(() => view.FileDialogs.Count > 0, TimeSpan.FromSeconds(5));
                if (view.FileDialogs.Count == 0)
                {
                    return;
                }

                new WebFileDialogRequestedEventArgs(view.FileDialogs[0]).AnswerFromFileDialog();
                Pump(() => view.Title?.StartsWith("got:") == true, TimeSpan.FromSeconds(5));
                title = view.Title;
            }));
        }
        finally
        {
            Adamantium.UI.Core.FileDialog.Platform = before;
        }

        Assert.Multiple(() =>
        {
            Assert.That(platform.Offered?.SelectMany(type => type.Extensions), Is.EqualTo(new[] { "txt" }));
            Assert.That(title, Is.EqualTo("got:platform.txt"));
        });
    }

    // A field taking several files asks for several, and gets them all.
    [Test]
    public void AFieldTakingSeveralFiles_GetsThemAll()
    {
        const string page = "data:text/html,<input type=file multiple " +
                            "onchange=\"document.title='n:'+this.files.length\" style='display:block;width:100%;height:100vh'>";
        Directory.CreateDirectory(profile);
        var first = Path.Combine(profile, "one.txt");
        var second = Path.Combine(profile, "two.txt");
        File.WriteAllText(first, "1");
        File.WriteAllText(second, "2");
        WebFileDialogMode? mode = null;
        string title = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            ClickTheMiddle(browser);
            Pump(() => view.FileDialogs.Count > 0, TimeSpan.FromSeconds(5));
            if (view.FileDialogs.Count == 0)
            {
                return;
            }

            mode = view.FileDialogs[0].Mode;
            view.FileDialogs[0].Choose([first, second]);
            Pump(() => view.Title?.StartsWith("n:") == true, TimeSpan.FromSeconds(5));
            title = view.Title;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(mode, Is.EqualTo(WebFileDialogMode.OpenMultiple));
            Assert.That(title, Is.EqualTo("n:2"));
        });
    }

    // A site asking where the device is asks the view once: the answer is kept, the site gets it unasked next time, and
    // once taken back it is asked again. Needs the network: example.org is a site, a data address is none.
    [Test]
    public void APermissionAnswered_IsKeptForTheSite_UntilTakenBack()
    {
        const string site = "https://example.org";
        const string ask = "navigator.geolocation.getCurrentPosition(() => document.title = 'where', " +
                           "e => document.title = 'error ' + e.code), 1";
        WebPermission? asked = null;
        string askedSite = null, refused = null, refusedAgain = null;
        int askedFirst = 0, askedSecond = 0, askedAfterForgetting = 0, askedAfterRefusing = 0;
        bool kept = false, forgotten = false;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, site + "/", (browser, view) =>
        {
            Pump(() => view.Title == "Example Domain", TimeSpan.FromSeconds(15));
            Await(browser.EvaluateAsync(ask));
            Pump(() => view.Permissions.Count > 0, TimeSpan.FromSeconds(5));
            askedFirst = view.Permissions.Count;
            if (askedFirst == 0)
            {
                return;
            }

            asked = view.Permissions[0].Permissions;
            askedSite = view.Permissions[0].Site;
            view.Permissions[0].Decide(false);
            Pump(() => view.Title.StartsWith("error"), TimeSpan.FromSeconds(5));
            refused = view.Title;
            kept = engine.SitePermissions.Any(p => p.Site == site && p.Permission == WebPermission.Geolocation && !p.IsAllowed);

            Await(browser.EvaluateAsync("document.title = 'again', 1"));
            Await(browser.EvaluateAsync(ask));
            Pump(() => view.Title.StartsWith("error"), TimeSpan.FromSeconds(5));
            askedSecond = view.Permissions.Count;
            refusedAgain = view.Title;

            engine.ForgetSitePermission(site, WebPermission.Geolocation);
            forgotten = !engine.SitePermissions.Any(p => p.Site == site);
            Await(browser.EvaluateAsync(ask));
            Pump(() => view.Permissions.Count > 1, TimeSpan.FromSeconds(5));
            askedAfterForgetting = view.Permissions.Count;
            // Refused by nobody's answer - the view model let it be: kept nowhere, Chromium included.
            view.Permissions.LastOrDefault()?.Answer(false);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
            Await(browser.EvaluateAsync(ask));
            Pump(() => view.Permissions.Count > 2, TimeSpan.FromSeconds(5));
            askedAfterRefusing = view.Permissions.Count;
            view.Permissions.LastOrDefault()?.Answer(false);
        }));

        Assert.Multiple(() =>
        {
            Assert.That(askedFirst, Is.EqualTo(1), "asked of the view");
            Assert.That((asked, askedSite), Is.EqualTo(((WebPermission?)WebPermission.Geolocation, site)));
            Assert.That(refused, Is.EqualTo("error 1"), "PERMISSION_DENIED");
            Assert.That(kept, Is.True, "the answer is kept for the site");
            Assert.That(askedSecond, Is.EqualTo(1), "not asked again");
            Assert.That(refusedAgain, Is.EqualTo("error 1"), "the kept answer is given");
            Assert.That(forgotten, Is.True);
            Assert.That(askedAfterForgetting, Is.EqualTo(2), "taken back, asked again");
            Assert.That(askedAfterRefusing, Is.EqualTo(3), "refused unanswered, asked again");
        });
    }

    // Refused again and again without the user's answer - a question put away, a view that asks nothing - a site is
    // still asked: Chromium blocks a site for days after a few prompts the user let go, and nothing here could undo it.
    // Needs the network, as above.
    [Test]
    public void ASiteRefusedUnansweredOften_IsStillAsked()
    {
        const string ask = "navigator.geolocation.getCurrentPosition(() => document.title = 'where', " +
                           "e => document.title = 'error ' + e.code), 1";
        var asked = 0;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, "https://example.com/", (browser, view) =>
        {
            Pump(() => view.Title == "Example Domain", TimeSpan.FromSeconds(15));
            for (var round = 1; round <= 5; round++)
            {
                Await(browser.EvaluateAsync(ask));
                var expected = round;
                Pump(() => view.Permissions.Count >= expected, TimeSpan.FromSeconds(5));
                view.Permissions.LastOrDefault()?.Answer(false);
                Pump(() => false, TimeSpan.FromMilliseconds(200));
            }

            asked = view.Permissions.Count;
        }));

        Assert.That(asked, Is.EqualTo(5), "asked each time");
    }

    // An element's title comes to the view as its tooltip while the pointer is over it.
    [Test]
    public void AnElementsTitle_ComesAsTheTooltip()
    {
        const string page = "data:text/html,<body style='margin:0'><div title='Hint' style='height:100vh'>x</div></body>";
        string tooltip = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            browser.SendMouseMove(Width / 2, Height / 2, InputModifiers.None, false);
            browser.SendMouseMove(Width / 2 + 2, Height / 2 + 2, InputModifiers.None, false);
            Pump(() => !string.IsNullOrEmpty(view.Tooltip), TimeSpan.FromSeconds(5));
            tooltip = view.Tooltip;
        }));

        Assert.That(tooltip, Is.EqualTo("Hint"));
    }

    // Inspecting a point shows the DevTools front end the element there, as its inspect mode does.
    [Test]
    public void InspectingAPoint_ShowsTheFrontEndTheElementThere()
    {
        const string page = "data:text/html,<body style='margin:0'><p id='target' style='height:100vh'>x</p></body>";
        string revealed = null, described = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, _) =>
        {
            var address = browser.OpenDevTools();
            var socketAddress = new Uri("ws://" + address[(address.IndexOf("ws=", StringComparison.Ordinal) + 3)..]);
            using var frontEnd = new System.Net.WebSockets.ClientWebSocket();
            frontEnd.Options.SetRequestHeader("Origin", "devtools://devtools");
            var connected = frontEnd.ConnectAsync(socketAddress, CancellationToken.None);
            Pump(() => connected.IsCompleted, TimeSpan.FromSeconds(5));
            browser.InspectAt(Width / 2, Height / 2);
            foreach (var call in new[] { "{\"id\":1,\"method\":\"DOM.enable\",\"params\":{}}",
                                         "{\"id\":2,\"method\":\"Overlay.enable\",\"params\":{}}" })
            {
                frontEnd.SendAsync(System.Text.Encoding.UTF8.GetBytes(call),
                    System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).Wait();
            }

            revealed = Receive(frontEnd, reply => reply.Contains("Overlay.inspectNodeRequested"));
            if (revealed == null)
            {
                return;
            }

            var node = System.Text.Json.JsonDocument.Parse(revealed).RootElement.GetProperty("params")
                .GetProperty("backendNodeId").GetInt32();
            var describe = $"{{\"id\":3,\"method\":\"DOM.describeNode\",\"params\":{{\"backendNodeId\":{node}}}}}";
            frontEnd.SendAsync(System.Text.Encoding.UTF8.GetBytes(describe),
                System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).Wait();
            described = Receive(frontEnd, reply => reply.StartsWith("{\"id\":3,", StringComparison.Ordinal));
        }));

        Assert.Multiple(() =>
        {
            Assert.That(revealed, Does.Contain("backendNodeId"));
            Assert.That(described, Does.Contain("\"target\""), "the element under the point");
        });
    }

    // The front end's Styles pane: the element's matched rules come, as the DOM and CSS domains give them.
    [Test]
    public void TheDevToolsFrontEnd_GetsAnElementsStyles()
    {
        // Nested deeper than a JSON reader's default depth, as a real site's document is.
        var page = "data:text/html,<style>p{color:rgb(1,2,3)}</style>" + string.Concat(Enumerable.Repeat("<div>", 80)) +
                   "<p>styled</p>";
        string styles = null, deep = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, _) =>
        {
            var address = browser.OpenDevTools();
            var socketAddress = new Uri("ws://" + address[(address.IndexOf("ws=", StringComparison.Ordinal) + 3)..]);
            using var frontEnd = new System.Net.WebSockets.ClientWebSocket();
            frontEnd.Options.SetRequestHeader("Origin", "devtools://devtools");
            var connected = frontEnd.ConnectAsync(socketAddress, CancellationToken.None);
            Pump(() => connected.IsCompleted, TimeSpan.FromSeconds(5));

            string Ask(int id, string method, string parameters)
            {
                var call = $"{{\"id\":{id},\"method\":\"{method}\",\"params\":{parameters}}}";
                frontEnd.SendAsync(System.Text.Encoding.UTF8.GetBytes(call),
                    System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).Wait();
                return Receive(frontEnd, reply => reply.StartsWith($"{{\"id\":{id},", StringComparison.Ordinal));
            }

            Ask(1, "DOM.enable", "{}");
            Ask(2, "CSS.enable", "{}");
            deep = Ask(6, "DOM.getDocument", "{\"depth\":-1}");
            var document = Ask(3, "DOM.getDocument", "{}");
            var root = System.Text.Json.JsonDocument.Parse(document).RootElement.GetProperty("result")
                .GetProperty("root").GetProperty("nodeId").GetInt32();
            var found = Ask(4, "DOM.querySelector", $"{{\"nodeId\":{root},\"selector\":\"p\"}}");
            var node = System.Text.Json.JsonDocument.Parse(found).RootElement.GetProperty("result")
                .GetProperty("nodeId").GetInt32();
            styles = Ask(5, "CSS.getMatchedStylesForNode", $"{{\"nodeId\":{node}}}");
        }));

        Assert.Multiple(() =>
        {
            Assert.That(deep, Does.Contain("\"nodeName\":\"P\""), "the whole document, however deep");
            Assert.That(styles, Does.Contain("rgb(1, 2, 3)").Or.Contain("rgb(1,2,3)"));
        });
    }

    private static string Receive(System.Net.WebSockets.ClientWebSocket socket, Func<string, bool> wanted)
    {
        var buffer = new byte[256 * 1024];
        var message = new MemoryStream();
        var until = Environment.TickCount64 + 5000;
        while (Environment.TickCount64 < until)
        {
            var received = socket.ReceiveAsync(buffer, CancellationToken.None);
            Pump(() => received.IsCompleted, TimeSpan.FromMilliseconds(until - Environment.TickCount64));
            if (!received.IsCompletedSuccessfully)
            {
                return null;
            }

            message.Write(buffer, 0, received.Result.Count);
            if (!received.Result.EndOfMessage)
            {
                continue;
            }

            var text = System.Text.Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            message.SetLength(0);
            if (wanted(text))
            {
                return text;
            }
        }

        return null;
    }

    // A navigation the view model refuses keeps the page where it is; HTML shown from a string loads.
    [Test]
    public void ARefusedNavigation_KeepsThePage_AndHtmlFromAStringLoads()
    {
        const string page = "data:text/html,<title>Here</title>";
        string titleAfterRefusal = null, titleOfHtml = null;
        bool asked = false;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            Pump(() => view.Title == "Here", TimeSpan.FromSeconds(5));
            view.Refuse = address => address.Contains("Elsewhere");
            browser.Navigate("data:text/html,<title>Elsewhere</title>");
            Pump(() => false, TimeSpan.FromMilliseconds(800));
            asked = view.Started.Any(address => address.Contains("Elsewhere"));
            titleAfterRefusal = view.Title;
            browser.LoadHtml("<title>From a string</title><p>hello</p>");
            Pump(() => view.Title == "From a string", TimeSpan.FromSeconds(5));
            titleOfHtml = view.Title;
        }));

        Assert.Multiple(() =>
        {
            Assert.That(asked, Is.True, "the view model was asked");
            Assert.That(titleAfterRefusal, Is.EqualTo("Here"), "the page stayed");
            Assert.That(titleOfHtml, Is.EqualTo("From a string"));
        });
    }

    private static string Await(Task<string> answer)
    {
        Pump(() => answer.IsCompleted, TimeSpan.FromSeconds(5));
        return answer.IsCompletedSuccessfully ? answer.Result : null;
    }

    // The pointer over a link puts the link's address in the status.
    [Test]
    public void ThePointerOverALink_ShowsItsAddress()
    {
        const string page = "data:text/html,<body style='margin:0'><a href='https://example.org/target' " +
                            "style='display:block;height:48px'>link</a></body>";
        string status = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (browser, view) =>
        {
            browser.SendMouseMove(Width / 2, Height / 2, InputModifiers.None, false);
            Pump(() => !string.IsNullOrEmpty(view.Status), TimeSpan.FromSeconds(3));
            status = view.Status;
        }));

        Assert.That(status, Is.EqualTo("https://example.org/target"));
    }

    // The icon a page names comes to the view at a tab's size, in its colors; a page over no encryption is not secure.
    [Test]
    public void ThePagesIcon_ComesAtATabsSize_AndAnOpenPageIsNotSecure()
    {
        var page = "data:text/html,<html><head><link rel='icon' href='data:image/bmp;base64," +
                   Convert.ToBase64String(RedBitmap(16)) + "'></head><body>icon</body></html>";
        BitmapSource icon = null;
        var secure = true;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (_, view) =>
        {
            Pump(() => view.Icon != null, TimeSpan.FromSeconds(5));
            icon = view.Icon as BitmapSource;
            secure = view.IsSecure;
        }));

        Assert.That(icon, Is.Not.Null, "the icon came");
        var middle = ((int)icon.PixelHeight / 2 * (int)icon.PixelWidth + (int)icon.PixelWidth / 2) * 4;
        Assert.Multiple(() =>
        {
            Assert.That(icon.Width, Is.EqualTo(16).Within(0.01), "a tab's size");
            Assert.That(icon.PixelBytes[middle..(middle + 4)], Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "red, BGRA");
            Assert.That(secure, Is.False);
        });
    }

    // An icon that does not come gives way to the page's next.
    [Test]
    public void AnIconThatDoesNotCome_GivesWayToThePagesNext()
    {
        var page = "data:text/html,<html><head><link rel='icon' href='data:image/bmp;base64,AAAA'>" +
                   "<link rel='icon' href='data:image/bmp;base64," + Convert.ToBase64String(RedBitmap(16)) +
                   "'></head><body>icons</body></html>";
        BitmapSource icon = null;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, page, (_, view) =>
        {
            Pump(() => view.Icon != null, TimeSpan.FromSeconds(5));
            icon = view.Icon as BitmapSource;
        }));

        Assert.That(icon?.PixelBytes[..4], Is.EqualTo(new byte[] { 0, 0, 255, 255 }));
    }

    // A bitmap file of one red square, 24 bits a pixel.
    private static byte[] RedBitmap(int side)
    {
        var row = side * 3;
        var pixels = row * side;
        var file = new byte[54 + pixels];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BitConverter.TryWriteBytes(file.AsSpan(2), file.Length);
        BitConverter.TryWriteBytes(file.AsSpan(10), 54);
        BitConverter.TryWriteBytes(file.AsSpan(14), 40);
        BitConverter.TryWriteBytes(file.AsSpan(18), side);
        BitConverter.TryWriteBytes(file.AsSpan(22), side);
        BitConverter.TryWriteBytes(file.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(file.AsSpan(28), (short)24);
        BitConverter.TryWriteBytes(file.AsSpan(34), pixels);
        for (var i = 54; i < file.Length; i += 3)
        {
            file[i + 2] = 255;
        }

        return file;
    }

    private (WebViewFramePath Path, int Frames, bool Refused, byte[] Center) ShowPage(WebViewFramePath path, string page,
        Action<WebViewBrowser> act, Action<RecordingBrowserView> look = null) =>
        ShowPage(path, page, act == null ? null : (browser, _) => act(browser), look);

    private (WebViewFramePath Path, int Frames, bool Refused, byte[] Center) ShowPage(WebViewFramePath path) =>
        ShowPage(path, RedPage, (Action<WebViewBrowser, RecordingBrowserView>)null);

    private (WebViewFramePath Path, int Frames, bool Refused, byte[] Center) ShowPage(WebViewFramePath path, string page,
        Action<WebViewBrowser, RecordingBrowserView> act, Action<RecordingBrowserView> look = null, Color? background = null)
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

            if (background is { } color)
            {
                browser.Background = color;
            }

            browser.Create(page, Width, Height, 1, IntPtr.Zero);
            Pump(() => view.Frames > 0 || view.Refused, TimeSpan.FromSeconds(30), view, consumer);
            Pump(() => false, TimeSpan.FromMilliseconds(500), view, consumer);
            if (act != null)
            {
                act(browser, view);
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

    private static bool IsRed(byte[] bgra) => bgra != null && bgra.AsSpan().SequenceEqual(new byte[] { 0, 0, 255, 255 });

    private static bool Pump(Func<bool> done, TimeSpan timeout, RecordingBrowserView view = null,
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

        return done();
    }
}
