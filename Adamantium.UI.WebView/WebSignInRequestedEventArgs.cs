namespace Adamantium.UI.WebView;

/// <summary>
/// A site or a proxy asks the page's user to sign in. Raised before anything else is asked: the application can answer
/// with <see cref="Credential"/> or refuse with <see cref="Cancel"/>; left alone, the view answers from
/// <see cref="WebViewEngine.Credentials"/> or asks its user in the sign-in dialog over the page.
/// </summary>
public sealed class WebSignInRequestedEventArgs : EventArgs
{
    internal WebSignInRequestedEventArgs(WebSignInTarget target, string address, bool isRetry)
    {
        Target = target;
        Address = address;
        IsRetry = isRetry;
    }

    /// <summary>Who asks.</summary>
    public WebSignInTarget Target { get; }

    /// <summary>The address the page was going to when it was asked.</summary>
    public string Address { get; }

    /// <summary>Whether it asks again right after an answer: the name or the password it was given was refused.</summary>
    public bool IsRetry { get; }

    /// <summary>Signs in with this, asking nobody.</summary>
    public WebCredential Credential { get; set; }

    /// <summary>Refuses, asking nobody: the page gets the server's refusal as it is.</summary>
    public bool Cancel { get; set; }
}
