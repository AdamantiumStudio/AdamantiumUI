namespace Adamantium.UI.WebView.Cef;

/// <summary>One row of the menu a page asks for, read out of Chromium's model while it is asked: a command with its
/// label, whether it is on and ticked, a separator, or a submenu with its rows.</summary>
internal sealed record WebViewMenuItem(
    int Command,
    string Label,
    WebViewMenuItemKind Kind,
    bool IsEnabled,
    bool IsChecked,
    IReadOnlyList<WebViewMenuItem> Children);
