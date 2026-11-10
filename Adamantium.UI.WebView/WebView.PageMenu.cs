using Adamantium.Mathematics;
using Adamantium.UI.Controls;
using Adamantium.UI.Controls.Primitives;
using Adamantium.UI.Core;
using Adamantium.UI.Core.Dispatcher;
using Adamantium.UI.Core.RoutedEvents;
using Adamantium.UI.WebView.Cef;
using Adamantium.UI.WebView.Localization;

namespace Adamantium.UI.WebView;

/// <summary>The page's menu and tooltips, shown by the view: Chromium's menu for a point of the page in the template's
/// <c>PART_PageMenu</c>, a row for the DevTools at the element after it, and the page's tooltip in
/// <c>PART_PageTip</c> beside the pointer - apart from the view's own tooltip, which the application may give it.
/// </summary>
public partial class WebView
{
    private const double PageTipBelowPointer = 20;

    private ContextMenu pageMenu;
    private WebViewContextMenu askedMenu;
    private Popup pageTipPopup;
    private ToolTip pageTip;
    private DispatcherTimer pageTipTimer;
    private string pageTipText;
    private Vector2<double> pointerAt;

    private void TakePageMenu(ContextMenu menu, Popup tipPopup, ToolTip tip)
    {
        if (pageMenu != null)
        {
            pageMenu.PropertyChanged -= OnPageMenuChanged;
            DismissPageMenu();
        }

        HidePageTip();
        pageMenu = menu;
        pageTipPopup = tipPopup;
        pageTip = tip;
        if (pageMenu != null)
        {
            pageMenu.PropertyChanged += OnPageMenuChanged;
        }
    }

    bool IWebViewBrowserView.ContextMenuRequested(WebViewBrowser from, WebViewContextMenu menu)
    {
        if (!ReferenceEquals(browser, from) || pageMenu == null)
        {
            return false;
        }

        // A menu asked for while one is open takes its place, at its own point.
        if (pageMenu.IsOpen)
        {
            pageMenu.IsOpen = false;
        }

        DismissPageMenu();
        HidePageTip();
        askedMenu = menu;
        pageMenu.Items.Clear();
        if (!string.IsNullOrEmpty(menu.LinkAddress))
        {
            var link = menu.LinkAddress;
            pageMenu.Items.Add(Row(WebViewStrings.OpenLinkInNewTab, true, () =>
            {
                menu.Dismiss();
                OpenNewPage(link, WebNewWindowKind.BackgroundTab);
            }));
            pageMenu.Items.Add(new Separator());
        }

        foreach (var row in Rows(menu, menu.Items))
        {
            pageMenu.Items.Add(row);
        }

        if (pageMenu.Items.Count > 0)
        {
            pageMenu.Items.Add(new Separator());
        }

        pageMenu.Items.Add(Row(WebViewStrings.Inspect, true, () => Inspect(menu)));
        // In the view's own coordinates, as the page is given the pointer.
        pageMenu.Open(this, new Vector2<double>(menu.X, menu.Y));
        return true;
    }

    void IWebViewBrowserView.TooltipChanged(WebViewBrowser from, string text)
    {
        if (!ReferenceEquals(browser, from))
        {
            return;
        }

        pageTipText = text;
        if (string.IsNullOrEmpty(text) || pageTip == null || pageTipPopup == null)
        {
            HidePageTip();
            return;
        }

        // A tip up already changes at once; a new one waits the delay a hover waits, as the view's own would.
        if (pageTipPopup.IsOpen)
        {
            ShowPageTip();
            return;
        }

        if (pageTipTimer == null)
        {
            pageTipTimer = new DispatcherTimer();
            pageTipTimer.Tick += OnPageTipDue;
        }

        pageTipTimer.Stop();
        pageTipTimer.Start(TimeSpan.FromMilliseconds(ToolTipService.GetInitialShowDelay(this)));
    }

    private void OnPageTipDue(object sender, EventArgs e)
    {
        pageTipTimer.Stop();
        if (!string.IsNullOrEmpty(pageTipText) && IsPointerOver && askedMenu == null)
        {
            ShowPageTip();
        }
    }

    private void ShowPageTip()
    {
        pageTip.Content = pageTipText;
        pageTipPopup.PlacementTarget = this;
        pageTipPopup.HorizontalOffset = pointerAt.X;
        pageTipPopup.VerticalOffset = pointerAt.Y + PageTipBelowPointer;
        pageTipPopup.IsOpen = true;
    }

    private void HidePageTip()
    {
        pageTipTimer?.Stop();
        pageTipText = null;
        if (pageTipPopup != null)
        {
            pageTipPopup.IsOpen = false;
        }

        if (pageTip != null)
        {
            pageTip.Content = null;
        }
    }

    private IEnumerable<object> Rows(WebViewContextMenu menu, IReadOnlyList<WebViewMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.Kind == WebViewMenuItemKind.Separator)
            {
                yield return new Separator();
                continue;
            }

            var command = item.Command;
            // Off screen Chromium prints nothing; its page source it opens in a window of its own, which off screen it
            // does not make - the view opens it as a page of its own instead.
            if (command == (int)CefMenuId.Print)
            {
                continue;
            }

            Action chosen = item.Kind == WebViewMenuItemKind.Submenu ? null
                : command == (int)CefMenuId.ViewSource ? () => ViewSource(menu)
                : () => menu.Choose(command);
            var row = Row(item.Label, item.IsEnabled, chosen);
            if (item.Kind == WebViewMenuItemKind.Check)
            {
                row.IsCheckable = true;
                row.IsChecked = item.IsChecked;
            }

            foreach (var child in Rows(menu, item.Children))
            {
                row.Items.Add(child);
            }

            yield return row;
        }
    }

    private static MenuItem Row(string label, bool enabled, Action chosen)
    {
        var row = new MenuItem { Header = label, IsEnabled = enabled };
        if (chosen != null)
        {
            row.Click += (_, _) => chosen();
        }

        return row;
    }

    private void ViewSource(WebViewContextMenu menu)
    {
        menu.Dismiss();
        if (browser?.Address is { Length: > 0 } address)
        {
            OpenNewPage("view-source:" + address, WebNewWindowKind.ForegroundTab);
        }
    }

    // The DevTools at the element the menu was asked for over: found before they open, as they lay the page out anew.
    private void Inspect(WebViewContextMenu menu)
    {
        menu.Dismiss();
        browser?.InspectAt(menu.X, menu.Y);
        SetCurrentValue(IsDevToolsOpenProperty, true);
    }

    // A menu let go without a row chosen - a press elsewhere, Esc - is answered with none.
    private void OnPageMenuChanged(object sender, AdamantiumPropertyChangedEventArgs e)
    {
        if (e.Property == ContextMenu.IsOpenProperty && e.NewValue is false)
        {
            DismissPageMenu();
        }
    }

    private void DismissPageMenu()
    {
        askedMenu?.Dismiss();
        askedMenu = null;
    }
}
