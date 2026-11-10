namespace Adamantium.UI.WebView;

/// <summary>
/// A request the page is about to make - its document, a frame's, a script, an image, a fetch - which a handler may
/// answer itself, send elsewhere or refuse; left alone, it goes on as asked. Raised on the UI thread while the request
/// waits; the last call of a handler decides.
/// </summary>
public sealed class WebResourceRequestedEventArgs : EventArgs
{
    internal WebResourceRequestedEventArgs(string address, string method, WebResourceKind kind, bool isMainFrame)
    {
        Address = address;
        Method = method;
        Kind = kind;
        IsMainFrame = isMainFrame;
    }

    /// <summary>Where the request goes.</summary>
    public string Address { get; }

    /// <summary>Its HTTP method: GET, POST...</summary>
    public string Method { get; }

    /// <summary>What it is for.</summary>
    public WebResourceKind Kind { get; }

    /// <summary>Whether the page's main frame makes it, not one of its frames.</summary>
    public bool IsMainFrame { get; }

    internal Stream Content { get; private set; }

    internal string MimeType { get; private set; }

    internal int Status { get; private set; }

    internal string RedirectedTo { get; private set; }

    internal bool Answered { get; private set; }

    internal bool Refused { get; private set; }

    /// <summary>Answers the request with <paramref name="content"/> - read from its start, disposed once read; none is
    /// an empty answer - typed <paramref name="mimeType"/>, with <paramref name="status"/>.</summary>
    public void Respond(Stream content, string mimeType, int status = 200)
    {
        ArgumentException.ThrowIfNullOrEmpty(mimeType);
        Decide();
        Answered = true;
        Content = content;
        MimeType = mimeType;
        Status = status;
    }

    /// <summary>Sends the request to <paramref name="address"/> instead, as a redirect would.</summary>
    public void Redirect(string address)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        Decide();
        RedirectedTo = address;
    }

    /// <summary>Refuses the request: it fails, as one the network did not answer.</summary>
    public void Refuse()
    {
        Decide();
        Refused = true;
    }

    // A decision taken again forgets the last; an answer given before is not read, and is let go of.
    private void Decide()
    {
        Content?.Dispose();
        Content = null;
        MimeType = null;
        Answered = false;
        RedirectedTo = null;
        Refused = false;
    }
}
