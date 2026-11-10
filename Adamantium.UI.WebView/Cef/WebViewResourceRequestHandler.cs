using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Adamantium.UI.Core;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The page's requests, asked of the view when it listens: each waits - on CEF's IO thread, without holding it - while
/// the view decides on the UI thread, then goes on as asked, elsewhere, refused, or answered by the view itself.
/// </summary>
internal sealed unsafe class WebViewResourceRequestHandler : CefObject
{
    private readonly WebViewBrowser browser;
    private readonly ConcurrentDictionary<ulong, (Stream Content, string MimeType, int Status)> answers = new();

    public WebViewResourceRequestHandler(WebViewBrowser browser) : base(sizeof(CefResourceRequestHandler))
    {
        this.browser = browser;
        var handler = (CefResourceRequestHandler*)Native;
        handler->on_before_resource_load = &OnBeforeResourceLoad;
        handler->get_resource_handler = &GetResourceHandler;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefResourceRequestHandler* Shared => (CefResourceRequestHandler*)Share();

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
            LoopSignal.Post(() => handler.Decide(asked, id, request, callback));
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

    private void Decide(WebResourceRequestedEventArgs asked, ulong id, CefRequest* request, CefCallback* callback)
    {
        var refuse = false;
        try
        {
            if (browser.IsClosing)
            {
                refuse = true;
            }
            else
            {
                browser.OnResourceRequested(asked);
                refuse = asked.Refused;
                if (asked.RedirectedTo != null)
                {
                    var address = new CefStringUtf16();
                    try
                    {
                        CefStrings.Set(&address, asked.RedirectedTo);
                        request->set_url(request, &address);
                    }
                    finally
                    {
                        CefStrings.Clear(&address);
                    }
                }
                else if (asked.Answered)
                {
                    answers[id] = (asked.Content, asked.MimeType, asked.Status);
                }
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
        finally
        {
            if (refuse)
            {
                callback->cancel(callback);
            }
            else
            {
                callback->cont(callback);
            }

            CefRefCounting.ReleaseCef(request);
            CefRefCounting.ReleaseCef(callback);
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
                answer = new WebViewResourceAnswer(given.Content, given.MimeType, given.Status).Shared;
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
