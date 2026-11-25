using System;
using Adamantium.Mathematics;
using Adamantium.UI.Controls.Panels;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Templates;
using NUnit.Framework;

namespace Adamantium.UI.WebView.Tests;

// A page another one opened, shown by the WebView it is given to - in a tree, with a template, as an application shows it.
public partial class WebViewFrameTests
{
    private const string OpensAWindow =
        "data:text/html,<body style='margin:0;height:100vh' onclick=\"w=window.open('','_blank');" +
        "w.document.write('<title>opener:'+(w.opener===window)+'</title><p>new</p>')\"></body>";

    private sealed class Root : Grid, IRootVisualComponent
    {
        public Vector2<double> PointToClient(PixelPoint point) => new(point.X, point.Y);
        public PixelPoint PointToScreen(Vector2<double> point) => new(point.X, point.Y);
        public PixelPoint Position { get; set; }
        public void AttachContextAndInitialize(IUIContext context) { }
        public double Left { get; set; }
        public double Top { get; set; }
        public string Title { get; set; }
        public double ClientWidth { get; set; }
        public double ClientHeight { get; set; }
        public IUIContext UIContext => null;
    }

    private WebView MakeView(WebViewNewPage page) => new()
    {
        Engine = engine,
        Bridge = new WebViewBridge(),
        AllowSharedTexture = false,
        NewPage = page,
        Template = new ControlTemplate(() =>
        {
            var surface = new RenderTargetPanel();
            var result = new TemplateResult { RootComponent = surface };
            result.RegisterName("PART_Surface", surface);
            return result;
        }),
    };

    // Shown at a size; taken out of the tree at the end, which closes its page.
    private static void Show(WebView view, double width, double height, Action act)
    {
        var root = new Root();
        root.Children.Add(view);
        try
        {
            root.Measure(new Size<double>(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            // The view may have been given no size yet as it joined the tree: its page starts now that it has one.
            Pump(() => false, TimeSpan.FromMilliseconds(100));
            act();
        }
        finally
        {
            root.Children.Remove(view);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
        }
    }

    [Test]
    public void APageAnotherOpened_IsTakenUpByTheViewItIsGivenTo()
    {
        string title = null, width = null, opener = null;
        bool takenUp = false;
        loop.Run(() => ShowPage(WebViewFramePath.PixelBuffer, OpensAWindow, (browser, view) =>
        {
            ClickTheMiddle(browser);
            Pump(() => view.NewPages.Count > 0, TimeSpan.FromSeconds(5));
            Assert.That(view.NewPages, Has.Count.EqualTo(1), "the page asked for a window");
            var page = view.NewPages[0].Page;
            var shown = MakeView(page);
            Show(shown, 320, 200, () =>
            {
                Pump(() => shown.Title?.StartsWith("opener") == true, TimeSpan.FromSeconds(10));
                title = shown.Title;
                takenUp = page.Take(out _) == null;
                for (var asking = 0; asking < 10 && width != "320"; asking++)
                {
                    width = Await(shown.Bridge.ExecuteScriptAsync("innerWidth"));
                    Pump(() => false, TimeSpan.FromMilliseconds(100));
                }

                opener = Await(shown.Bridge.ExecuteScriptAsync("window.opener !== null"));
            });
        }));

        Assert.Multiple(() =>
        {
            Assert.That(title, Is.EqualTo("opener:true"), "Chromium's own page, joined to the one that opened it");
            Assert.That(takenUp, Is.True, "nothing is left for another view to take up");
            Assert.That(width, Is.EqualTo("320"), "sized to the view");
            Assert.That(opener, Is.EqualTo("true"), "the view's bridge runs that page");
        });
    }

    // One opened by the view's own menu is made by the view that shows it, at its address.
    [Test]
    public void APageOfTheMenu_IsMadeAtItsAddress()
    {
        string title = null;
        using var site = new LocalSite("<title>Opened by the menu</title>");
        loop.Run(() =>
        {
            var shown = MakeView(new WebViewNewPage(site.Origin + "/page"));
            Show(shown, 320, 200, () =>
            {
                Pump(() => shown.Title == "Opened by the menu", TimeSpan.FromSeconds(15));
                title = shown.Title;
            });
        });

        Assert.That(title, Is.EqualTo("Opened by the menu"));
    }

    // The page's opener goes while it is shown: the page stays.
    [Test]
    public void APageAnotherOpened_OutlivesTheOneThatOpenedIt()
    {
        string answer = null;
        WebView shown = null;
        Root root = null;
        loop.Run(() =>
        {
            ShowPage(WebViewFramePath.PixelBuffer, OpensAWindow, (browser, view) =>
            {
                ClickTheMiddle(browser);
                Pump(() => view.NewPages.Count > 0, TimeSpan.FromSeconds(5));
                shown = MakeView(view.NewPages[0].Page);
                root = new Root();
                root.Children.Add(shown);
                root.Measure(new Size<double>(320, 200));
                root.Arrange(new Rect(0, 0, 320, 200));
                Pump(() => shown.Title?.StartsWith("opener") == true, TimeSpan.FromSeconds(10));
            });
            // The opener's browser is closed by now.
            answer = Await(shown.Bridge.ExecuteScriptAsync("document.body.innerText"));
            root.Children.Remove(shown);
            Pump(() => false, TimeSpan.FromMilliseconds(300));
        });

        Assert.That(answer, Is.EqualTo("\"new\""));
    }
}
