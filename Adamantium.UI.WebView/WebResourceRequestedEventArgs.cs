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

    internal IReadOnlyDictionary<string, string> Headers { get; private set; }

    internal string RedirectedTo { get; private set; }

    internal bool Answered { get; private set; }

    internal bool Refused { get; private set; }

    /// <summary>Answers the request with <paramref name="content"/> - read from its start, disposed once read; none is
    /// an empty answer - typed <paramref name="mimeType"/>, with <paramref name="status"/> and
    /// <paramref name="headers"/>: an answer to a page of another origin needs its
    /// <c>Access-Control-Allow-Origin</c>, as any site's.</summary>
    public void Respond(Stream content, string mimeType, int status = 200, IReadOnlyDictionary<string, string> headers = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(mimeType);
        Decide();
        Answered = true;
        Content = content;
        MimeType = mimeType;
        Status = status;
        Headers = headers;
    }

    /// <summary>Sends the request to <paramref name="address"/>, absolute, instead, as a redirect would; the request
    /// sent there is asked again - a handler that sends every request elsewhere sends it on for ever.</summary>
    public void Redirect(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out _))
        {
            throw new ArgumentException($"'{address}' is no absolute address.", nameof(address));
        }

        Decide();
        RedirectedTo = address;
    }

    // What a handler that threw had decided is let go of: the request is refused instead.
    internal void Discard() => Decide();

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
        Headers = null;
        Answered = false;
        RedirectedTo = null;
        Refused = false;
    }
}
