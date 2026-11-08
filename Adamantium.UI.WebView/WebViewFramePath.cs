namespace Adamantium.UI.WebView;

/// <summary>How a page's frames reach the texture it is shown by.</summary>
public enum WebViewFramePath
{
    /// <summary>No frame yet: the page is not made.</summary>
    None,

    /// <summary>Pixels in memory, written into the texture: everywhere.</summary>
    PixelBuffer,

    /// <summary>Chromium's own texture on the same GPU, copied there: where the device imports it.</summary>
    SharedTexture
}
