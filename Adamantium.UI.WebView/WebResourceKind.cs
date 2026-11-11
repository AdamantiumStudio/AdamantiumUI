namespace Adamantium.UI.WebView;

/// <summary>What a request a page makes is for.</summary>
public enum WebResourceKind
{
    /// <summary>Something else: a ping, a report, a prefetch.</summary>
    Other,

    /// <summary>The page itself, in the main frame.</summary>
    Document,

    /// <summary>A frame's document.</summary>
    Frame,

    /// <summary>A style sheet.</summary>
    Stylesheet,

    /// <summary>A script.</summary>
    Script,

    /// <summary>An image or an icon.</summary>
    Image,

    /// <summary>A font.</summary>
    Font,

    /// <summary>Sound or video.</summary>
    Media,

    /// <summary>What a script fetches, or asks with XMLHttpRequest.</summary>
    Fetch,

    /// <summary>A worker's script.</summary>
    Worker
}
