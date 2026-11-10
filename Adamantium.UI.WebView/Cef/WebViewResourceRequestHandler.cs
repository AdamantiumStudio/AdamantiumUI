using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The page's requests, asked of the view when it listens: each waits - on CEF's IO thread, without holding it - while
/// the view decides on the UI thread, then goes on as asked, elsewhere, refused, or answered by the view itself. A
/// handler that throws refuses the request; one the page gave up meanwhile is let go of; what still waits when the
/// browser closes is refused then, before CEF goes.
/// </summary>
internal sealed unsafe class WebViewResourceRequestHandler : CefObject
{
    private readonly WebViewBrowser browser;
    private readonly ConcurrentDictionary<ulong, WebViewWaitingRequest> waiting = new();
    private readonly ConcurrentDictionary<ulong, (Stream Content, string MimeType, int Status, IReadOnlyDictionary<string, string> Headers)> answers = new();

    public WebViewResourceRequestHandler(WebViewBrowser browser) : base(sizeof(CefResourceRequestHandler))
    {
        this.browser = browser;
        var handler = (CefResourceRequestHandler*)Native;
        handler->on_before_resource_load = &OnBeforeResourceLoad;
        handler->get_resource_handler = &GetResourceHandler;
        handler->on_resource_load_complete = &OnResourceLoadComplete;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefResourceRequestHandler* Shared => (CefResourceRequestHandler*)Share();

    /// <summary>Refuses every request still waiting for the view: the browser closes, and CEF may go before the view
    /// would decide. On the UI loop.</summary>
    public void RefuseWaiting()
    {
        foreach (var id in waiting.Keys)
        {
            if (waiting.TryRemove(id, out var asked))
            {
                asked.Finish(false);
                Release();
            }
        }
    }

    protected override void OnFreed()
    {
        foreach (var answer in answers.Values)
        {
            answer.Content?.Dispose();
        }

        answers.Clear();
    }

    [UnmanagedCallersOnly]
    private static CefReturnValue OnBeforeResourceLoad(CefResourceRequestHandler* self, CefBrowser* cefBrowser,
        CefFrame* frame, CefRequest* request, CefCallback* callback)
    {
        var waits = false;
        try
        {
            var handler = From<WebViewResourceRequestHandler>(self);
            var asked = new WebResourceRequestedEventArgs(CefStrings.ReadAndFree(request->get_url(request)),
                CefStrings.ReadAndFree(request->get_method(request)), Kind(request->get_resource_type(request)),
                frame != null && frame->is_main(frame) != 0);
            var id = request->get_identifier(request);

            // The request and the callback are kept until the view decides; they came with a reference each.
            handler.AddReference();
            handler.waiting[id] = new WebViewWaitingRequest(request, callback);
            LoopSignal.Post(() => handler.Decide(asked, id));
            waits = true;
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
        CefRefCounting.ReleaseCef(frame);
        if (waits)
        {
            return CefReturnValue.ContinueAsync;
        }

        CefRefCounting.ReleaseCef(request);
        CefRefCounting.ReleaseCef(callback);
        return CefReturnValue.Continue;
    }

    private void Decide(WebResourceRequestedEventArgs asked, ulong id)
    {
        // Refused already - the browser closed - or given up by the page meanwhile: nothing to decide.
        if (!waiting.TryRemove(id, out var request))
        {
            return;
        }

        var goesOn = false;
        try
        {
            if (!request.GivenUp && !browser.IsClosing)
            {
                browser.OnResourceRequested(asked);
                goesOn = !asked.Refused;
                if (asked.RedirectedTo != null)
                {
                    request.Redirect(asked.RedirectedTo);
                }
                else if (asked.Answered)
                {
                    answers[id] = (asked.Content, asked.MimeType, asked.Status, asked.Headers);
                }
            }
        }
        catch (Exception exception)
        {
            // A handler that throws decided nothing it meant: the request is refused, not let through.
            goesOn = false;
            asked.Discard();
            CefCallbacks.Report(exception);
        }
        finally
        {
            request.Finish(goesOn);
            Release();
        }
    }

    [UnmanagedCallersOnly]
    private static CefResourceHandler* GetResourceHandler(CefResourceRequestHandler* self, CefBrowser* cefBrowser,
        CefFrame* frame, CefRequest* request)
    {
        CefResourceHandler* answer = null;
        try
        {
            if (From<WebViewResourceRequestHandler>(self).answers.TryRemove(request->get_identifier(request), out var given))
            {
                answer = WebViewResourceAnswer.Make(given.Content, given.MimeType, given.Status, given.Headers);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(request);
        return answer;
    }

    // A request done - or given up - before its answer was taken: the answer is let go of; one still waiting for the
    // view is decided no more.
    [UnmanagedCallersOnly]
    private static void OnResourceLoadComplete(CefResourceRequestHandler* self, CefBrowser* cefBrowser, CefFrame* frame,
        CefRequest* request, CefResponse* response, CefUrlrequestStatus status, long receivedContentLength)
    {
        try
        {
            var handler = From<WebViewResourceRequestHandler>(self);
            var id = request->get_identifier(request);
            if (handler.waiting.TryGetValue(id, out var asked))
            {
                asked.GivenUp = true;
            }

            if (handler.answers.TryRemove(id, out var unused))
            {
                unused.Content?.Dispose();
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(cefBrowser);
        CefRefCounting.ReleaseCef(frame);
        CefRefCounting.ReleaseCef(request);
        CefRefCounting.ReleaseCef(response);
    }

    private static WebResourceKind Kind(CefResourceType type) => type switch
    {
        CefResourceType.MainFrame or CefResourceType.NavigationPreloadMainFrame => WebResourceKind.Document,
        CefResourceType.SubFrame or CefResourceType.NavigationPreloadSubFrame => WebResourceKind.Frame,
        CefResourceType.Stylesheet => WebResourceKind.Stylesheet,
        CefResourceType.Script => WebResourceKind.Script,
        CefResourceType.Image or CefResourceType.Favicon => WebResourceKind.Image,
        CefResourceType.FontResource => WebResourceKind.Font,
        CefResourceType.Media => WebResourceKind.Media,
        CefResourceType.Xhr => WebResourceKind.Fetch,
        CefResourceType.Worker or CefResourceType.SharedWorker or CefResourceType.ServiceWorker => WebResourceKind.Worker,
        _ => WebResourceKind.Other
    };
}
