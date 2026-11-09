using System.Runtime.InteropServices;
using System.Text;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A browser's menus: Chromium's menu for a point of the page is read into rows and shown by the view, its
/// callback answered with the row chosen or with none.</summary>
internal sealed unsafe class WebViewContextMenuHandler : CefObject
{
    private readonly WebViewBrowser browser;

    public WebViewContextMenuHandler(WebViewBrowser browser) : base(sizeof(CefContextMenuHandler))
    {
        this.browser = browser;
        ((CefContextMenuHandler*)Native)->run_context_menu = &RunContextMenu;
    }

    [UnmanagedCallersOnly]
    private static int RunContextMenu(CefContextMenuHandler* self, CefBrowser* cefBrowser, CefFrame* frame,
        CefContextMenuParams* parameters, CefMenuModel* model, CefRunContextMenuCallback* callback)
    {
        // Handled whatever comes: off screen Chromium has no menu of its own to fall back on, and a menu not shown is
        // answered with none, so the callback never stays held.
        var shown = 0;
        WebViewContextMenu menu = null;
        try
        {
            var owner = From<WebViewContextMenuHandler>(self).browser;
            if (owner.Is(cefBrowser))
            {
                menu = new WebViewContextMenu(parameters->get_xcoord(parameters), parameters->get_ycoord(parameters),
                    CefStrings.ReadAndFree(parameters->get_link_url(parameters)), Read(model), callback);
                shown = 1;
                if (!owner.OnRunContextMenu(menu))
                {
                    menu.Dismiss();
                }
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            menu?.Dismiss();
        }

        CefRefCounting.ReleaseCef(callback);
        CefRefCounting.ReleaseCef(model);
        CefRefCounting.ReleaseCef(parameters);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(cefBrowser);
        return shown;
    }

    // The visible rows of a model and its submenus, one level at a time: the menu's depth is the page's and small.
    private static IReadOnlyList<WebViewMenuItem> Read(CefMenuModel* model)
    {
        var count = (int)model->get_count(model);
        var items = new List<WebViewMenuItem>(count);
        for (ulong i = 0; i < (ulong)count; i++)
        {
            if (model->is_visible_at(model, i) == 0)
            {
                continue;
            }

            var type = model->get_type_at(model, i);
            if (type == CefMenuItemType.Separator)
            {
                items.Add(new WebViewMenuItem(0, string.Empty, WebViewMenuItemKind.Separator, false, false, []));
                continue;
            }

            if (type is not (CefMenuItemType.Command or CefMenuItemType.Check or CefMenuItemType.Radio or
                CefMenuItemType.Submenu))
            {
                continue;
            }

            IReadOnlyList<WebViewMenuItem> children = [];
            if (type == CefMenuItemType.Submenu)
            {
                var sub = model->get_sub_menu_at(model, i);
                if (sub != null)
                {
                    children = Read(sub);
                    CefRefCounting.ReleaseCef(sub);
                }
            }

            items.Add(new WebViewMenuItem(
                model->get_command_id_at(model, i),
                WithoutMnemonics(CefStrings.ReadAndFree(model->get_label_at(model, i))),
                type switch
                {
                    CefMenuItemType.Check or CefMenuItemType.Radio => WebViewMenuItemKind.Check,
                    CefMenuItemType.Submenu => WebViewMenuItemKind.Submenu,
                    _ => WebViewMenuItemKind.Command
                },
                model->is_enabled_at(model, i) != 0,
                model->is_checked_at(model, i) != 0,
                children));
        }

        return items;
    }

    // Chromium marks a label's access key with '&' and writes a real one as "&&".
    private static string WithoutMnemonics(string label)
    {
        if (label.IndexOf('&') < 0)
        {
            return label;
        }

        var text = new StringBuilder(label.Length);
        for (var i = 0; i < label.Length; i++)
        {
            if (label[i] != '&')
            {
                text.Append(label[i]);
            }
            else if (i + 1 < label.Length && label[i + 1] == '&')
            {
                text.Append('&');
                i++;
            }
        }

        return text.ToString();
    }
}
