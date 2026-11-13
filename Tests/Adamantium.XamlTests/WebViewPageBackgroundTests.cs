using System.Linq;
using System.Threading.Tasks;
using Adamantium.Mathematics;
using Adamantium.UI.Automation;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Decorators;
using Adamantium.UI.Core.Extensions;
using Adamantium.UI.Core.Media;
using Adamantium.UI.Themes.FluentTheme;
using Adamantium.UI.WebView;
using Adamantium.UI.WebView.Themes;
using NUnit.Framework;

namespace Adamantium.XamlTests;

/// <summary>
/// A WebView's page background lies under the page's frames: it shows where no frame is - before the first, or for a
/// page Chromium paints none of, as about:blank - not the theme's background under the view. No CEF here: the view has
/// no engine, so no frame ever comes. The view's own page host is the first in its tree; its DevTools have one too. The
/// view's look is added to each test's application: the view adds it once a process, to the application there was then.
/// </summary>
[TestFixture]
public class WebViewPageBackgroundTests
{
    [SetUp]
    public void Fresh() => HeadlessApplication.Start<Fluent>().ThemeManager.AddStyleSet<WebViewStyleSet>();

    [Test]
    public async Task ThePageBackground_ShowsWhereNoFrameIs()
    {
        var view = new WebView();
        await using var session = AutomationSession.InProcess(
            new Window { Width = 400, Height = 300, ClientWidth = 400, ClientHeight = 300, Content = view });
        await session.WaitForIdleAsync();
        var host = PageHost(view);
        var byDefault = (host.Background as SolidColorBrush)?.Color;

        view.PageBackground = new Color(30, 60, 90, 128);
        var chosen = (host.Background as SolidColorBrush)?.Color;
        view.PageBackground = Colors.Transparent;
        var transparent = host.Background;

        Assert.Multiple(() =>
        {
            Assert.That(byDefault, Is.EqualTo(Colors.White), "white, as in a browser");
            Assert.That(chosen, Is.EqualTo(new Color(30, 60, 90)), "any alpha but none is opaque, as the page's own");
            Assert.That(transparent, Is.Null, "fully transparent shows what lies under the view");
        });
    }

    private static Border PageHost(WebView view) =>
        view.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_PageHost");
}
