namespace Adamantium.UI.WebView;

/// <summary>How a page asked to open a new one.</summary>
public enum WebNewWindowKind
{
    /// <summary>A tab that comes to the front: a link with <c>target=_blank</c>, a row of the page's menu.</summary>
    ForegroundTab,

    /// <summary>A tab left behind the one open: a link opened with Ctrl or the middle button.</summary>
    BackgroundTab,

    /// <summary>A window of the page's own size and features: <c>window.open</c> with them - a sign-in, a payment.</summary>
    Popup,

    /// <summary>A window of its own: a link opened with Shift.</summary>
    Window
}
