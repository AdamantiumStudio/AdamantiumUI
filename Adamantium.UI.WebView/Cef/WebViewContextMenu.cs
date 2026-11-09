using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The menu a page asked for at a point of the view, waiting to be answered: its rows, and Chromium's callback held until
/// a row is chosen or the menu goes without one - either once. Lives on the UI loop, where CEF calls back.
/// </summary>
internal sealed unsafe class WebViewContextMenu
{
    /// <summary>The command the view adds itself: the DevTools, at the element under the menu.</summary>
    public const int InspectCommand = (int)CefMenuId.UserFirst;

    private CefRunContextMenuCallback* callback;

    public WebViewContextMenu(double x, double y, string linkAddress, IReadOnlyList<WebViewMenuItem> items,
        CefRunContextMenuCallback* callback)
    {
        X = x;
        Y = y;
        LinkAddress = linkAddress;
        Items = items;
        this.callback = callback;
        CefRefCounting.AddRefCef(callback);
    }

    /// <summary>Where on the page the menu was asked for, in device-independent pixels from its top left.</summary>
    public double X { get; }

    /// <inheritdoc cref="X"/>
    public double Y { get; }

    /// <summary>The address of the link the menu was asked for over; empty for none.</summary>
    public string LinkAddress { get; }

    /// <summary>The rows, as the page's menu has them.</summary>
    public IReadOnlyList<WebViewMenuItem> Items { get; }

    /// <summary>Whether it is answered.</summary>
    public bool IsAnswered => callback == null;

    /// <summary>Runs <paramref name="command"/>, a row's, in the page.</summary>
    public void Choose(int command)
    {
        if (callback == null)
        {
            return;
        }

        var held = callback;
        callback = null;
        held->cont(held, command, CefEventFlags.None);
        CefRefCounting.ReleaseCef(held);
    }

    /// <summary>Lets the menu go without a row chosen.</summary>
    public void Dismiss()
    {
        if (callback == null)
        {
            return;
        }

        var held = callback;
        callback = null;
        held->cancel(held);
        CefRefCounting.ReleaseCef(held);
    }
}
