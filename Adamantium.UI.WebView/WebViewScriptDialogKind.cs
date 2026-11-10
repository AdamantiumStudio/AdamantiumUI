namespace Adamantium.UI.WebView;

/// <summary>What a page's script asks with a dialog of its own.</summary>
public enum WebViewScriptDialogKind
{
    /// <summary>No dialog.</summary>
    None,

    /// <summary><c>alert</c>: a message, taken with OK.</summary>
    Alert,

    /// <summary><c>confirm</c>: a question, answered OK or Cancel.</summary>
    Confirm,

    /// <summary><c>prompt</c>: a question answered with text, or cancelled.</summary>
    Prompt,

    /// <summary>The page asks before it is left: leave it, or stay.</summary>
    LeavePage,

    /// <summary>The page asks before it is loaded again: reload it, or stay.</summary>
    ReloadPage
}
