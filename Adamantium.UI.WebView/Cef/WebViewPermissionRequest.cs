using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// A site asking to use something - the camera and the microphone through Chromium's media request, the rest through
/// its permission prompt: Chromium's callback held until the answer, once. An answer the user gives is kept for the
/// site (<see cref="Decide"/>); the browser's own - a page gone, a request refused unasked - is not
/// (<see cref="Answer"/>). Lives on the UI loop, where CEF calls back.
/// </summary>
internal sealed unsafe class WebViewPermissionRequest
{
    private readonly Action<WebViewPermissionRequest, bool> decided;
    private readonly Action<WebViewPermissionRequest> refusedUnasked;
    private readonly uint mediaAsked;
    private CefMediaAccessCallback* media;
    private CefPermissionPromptCallback* prompt;

    public WebViewPermissionRequest(string site, WebPermission permissions, CefMediaAccessCallback* media, uint mediaAsked,
        Action<WebViewPermissionRequest, bool> decided)
    {
        Site = site;
        Permissions = permissions;
        this.media = media;
        this.mediaAsked = mediaAsked;
        this.decided = decided;
        CefRefCounting.AddRefCef(media);
    }

    public WebViewPermissionRequest(string site, WebPermission permissions, ulong promptId,
        CefPermissionPromptCallback* prompt, Action<WebViewPermissionRequest, bool> decided,
        Action<WebViewPermissionRequest> refusedUnasked)
    {
        Site = site;
        Permissions = permissions;
        PromptId = promptId;
        this.prompt = prompt;
        this.decided = decided;
        this.refusedUnasked = refusedUnasked;
        CefRefCounting.AddRefCef(prompt);
    }

    /// <summary>The site asking, as its origin.</summary>
    public string Site { get; }

    /// <summary>What it asks to use.</summary>
    public WebPermission Permissions { get; }

    /// <summary>Which of Chromium's prompts it is; 0 for a media request.</summary>
    public ulong PromptId { get; }

    /// <summary>Whether it is answered or gone.</summary>
    public bool IsAnswered => media == null && prompt == null;

    /// <summary>Answered or gone: whoever shows it puts it away.</summary>
    public event EventHandler Closed;

    /// <summary>The user's answer: kept for the site, and given to the page.</summary>
    public void Decide(bool allowed)
    {
        if (IsAnswered)
        {
            return;
        }

        // Answered first: what is kept reaches Chromium's own settings, which would settle the request itself.
        Reply(allowed, blocked: !allowed);
        decided?.Invoke(this, allowed);
    }

    /// <summary>Gives the page <paramref name="allowed"/>, kept for nobody: a refusal is put away, as a prompt the user
    /// let go - Chromium's "block", which it keeps for the site, is the user's alone.</summary>
    public void Answer(bool allowed) => Reply(allowed, blocked: false);

    private void Reply(bool allowed, bool blocked)
    {
        if (media != null)
        {
            var held = media;
            media = null;
            if (allowed)
            {
                held->cont(held, mediaAsked);
            }
            else
            {
                held->cancel(held);
            }

            CefRefCounting.ReleaseCef(held);
        }
        else if (prompt != null)
        {
            var held = prompt;
            prompt = null;
            held->cont(held, allowed ? CefPermissionRequestResult.Accept
                : blocked ? CefPermissionRequestResult.Deny
                : CefPermissionRequestResult.Ignore);
            CefRefCounting.ReleaseCef(held);
            // Chromium counts prompts let go unanswered and blocks the site for days after a few: one the user never
            // saw is not counted against it.
            if (!allowed && !blocked)
            {
                refusedUnasked?.Invoke(this);
            }
        }
        else
        {
            return;
        }

        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The page went on: Chromium put its prompt away itself, nothing to answer; a media request is refused,
    /// kept for nobody.</summary>
    public void Withdraw()
    {
        if (media != null)
        {
            Answer(false);
            return;
        }

        if (prompt == null)
        {
            return;
        }

        var held = prompt;
        prompt = null;
        CefRefCounting.ReleaseCef(held);
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
