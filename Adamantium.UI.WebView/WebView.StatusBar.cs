using Adamantium.UI.Core;

namespace Adamantium.UI.WebView;

/// <summary>The view's status bar under the page: the link under the pointer, the zoom with a step either way and
/// back to 100%, and the DevTools.</summary>
public partial class WebView
{
    public static readonly AdamantiumProperty ShowStatusBarProperty = AdamantiumProperty.Register(nameof(ShowStatusBar),
        typeof(bool), typeof(WebView), new PropertyMetadata(true));

    /// <summary>Whether the status bar is shown under the page; true by default.</summary>
    public bool ShowStatusBar
    {
        get => GetValue<bool>(ShowStatusBarProperty);
        set => SetValue(ShowStatusBarProperty, value);
    }
}
