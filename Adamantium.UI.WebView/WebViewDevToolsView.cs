using Adamantium.UI.Core.Input;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>
/// The DevTools of a <see cref="WebView"/>'s page, shown inside that view beside the page: a view of its own, drawn and
/// driven as the page is, showing the DevTools front end, which talks to the page through a socket of the page's on
/// this machine. Made by the view's template; the view tells it whose DevTools it shows and when.
/// </summary>
public sealed class WebViewDevToolsView : WebView
{
    private WebView inspected;

    /// <summary>The view whose page this shows the DevTools of; set by that view, which starts and stops it.</summary>
    internal WebView Inspected
    {
        get => inspected;
        set
        {
            inspected = value;
            Restart();
        }
    }

    private protected override bool CanStartBrowser => inspected is { IsDevToolsOpen: true, Page: not null };

    private protected override void BeginBrowser(WebViewBrowser created, int width, int height, float scale,
        IntPtr parentWindow) =>
        created.CreateDevToolsFrontEnd(inspected.Page.OpenDevTools(), width, height, scale, parentWindow);

    // A press on the DevTools is theirs even before their front end is there: it must not reach the page beside them.
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
    }

    /// <summary>Opens or closes the DevTools to follow the inspected view: its page made again, or the DevTools asked
    /// for or put away.</summary>
    internal void Restart() => RestartBrowser();
}
