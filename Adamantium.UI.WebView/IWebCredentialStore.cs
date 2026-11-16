namespace Adamantium.UI.WebView;

/// <summary>
/// Where the application keeps the sign-ins its user asked to remember - the system's own store, at best. Given to
/// <see cref="WebViewEngine.Credentials"/>, it is asked before the sign-in dialog shows, and told when the user ticks
/// Remember there; without one nothing is remembered and the dialog offers nothing to tick.
/// </summary>
public interface IWebCredentialStore
{
    /// <summary>Whether a sign-in can be kept now - false where the system has no store to keep it in.</summary>
    bool CanSave { get; }

    /// <summary>What is kept for <paramref name="target"/>; null for nothing.</summary>
    WebCredential Find(WebSignInTarget target);

    /// <summary>Keeps <paramref name="credential"/> for <paramref name="target"/>, in place of what was kept.</summary>
    void Save(WebSignInTarget target, WebCredential credential);

    /// <summary>Forgets what is kept for <paramref name="target"/>: kept and refused, it is not offered again.</summary>
    void Forget(WebSignInTarget target);
}
