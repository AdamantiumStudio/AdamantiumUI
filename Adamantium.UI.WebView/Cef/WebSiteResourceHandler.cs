using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>One answer of a site of the application's: the file read as Chromium asks for it, typed as its extension
/// says, or 404 for none. Called on CEF's IO thread.</summary>
internal sealed unsafe class WebSiteResourceHandler : CefObject
{
    private readonly string mimeType;
    private Stream content;

    public WebSiteResourceHandler(Stream content, string mimeType) : base(sizeof(CefResourceHandler))
    {
        this.content = content;
        this.mimeType = mimeType;
        var handler = (CefResourceHandler*)Native;
        handler->open = &Open;
        handler->get_response_headers = &GetResponseHeaders;
        handler->skip = &Skip;
        handler->read = &Read;
        handler->cancel = &Cancel;
    }

    /// <summary>The struct with a reference taken for CEF.</summary>
    public CefResourceHandler* Shared => (CefResourceHandler*)Share();

    protected override void OnFreed() => Close();

    private void Close()
    {
        Interlocked.Exchange(ref content, null)?.Dispose();
    }

    [UnmanagedCallersOnly]
    private static int Open(CefResourceHandler* self, CefRequest* request, int* handleRequest, CefCallback* callback)
    {
        // The file is at hand: answered at once, nothing to wait for.
        *handleRequest = 1;
        CefRefCounting.ReleaseCef(request);
        CefRefCounting.ReleaseCef(callback);
        return 1;
    }

    [UnmanagedCallersOnly]
    private static void GetResponseHeaders(CefResourceHandler* self, CefResponse* response, long* responseLength,
        CefStringUtf16* redirectUrl)
    {
        try
        {
            var handler = From<WebSiteResourceHandler>(self);
            var content = handler.content;
            var type = new CefStringUtf16();
            var text = new CefStringUtf16();
            try
            {
                CefStrings.Set(&type, content == null ? "text/plain" : handler.mimeType);
                CefStrings.Set(&text, content == null ? "Not Found" : "OK");
                response->set_status(response, content == null ? 404 : 200);
                response->set_status_text(response, &text);
                response->set_mime_type(response, &type);
            }
            finally
            {
                CefStrings.Clear(&type);
                CefStrings.Clear(&text);
            }

            *responseLength = content == null ? 0 : content.CanSeek ? content.Length : -1;
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(response);
    }

    [UnmanagedCallersOnly]
    private static int Skip(CefResourceHandler* self, long bytesToSkip, long* bytesSkipped, CefResourceSkipCallback* callback)
    {
        CefRefCounting.ReleaseCef(callback);
        try
        {
            var content = From<WebSiteResourceHandler>(self).content;
            if (content == null)
            {
                *bytesSkipped = -2;
                return 0;
            }

            if (content.CanSeek)
            {
                var skipped = Math.Min(bytesToSkip, content.Length - content.Position);
                content.Position += skipped;
                *bytesSkipped = skipped;
                return skipped > 0 ? 1 : 0;
            }

            var buffer = new byte[Math.Min(bytesToSkip, 64 * 1024)];
            var read = content.Read(buffer, 0, buffer.Length);
            *bytesSkipped = read;
            return read > 0 ? 1 : 0;
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            *bytesSkipped = -2;
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static int Read(CefResourceHandler* self, void* dataOut, int bytesToRead, int* bytesRead,
        CefResourceReadCallback* callback)
    {
        CefRefCounting.ReleaseCef(callback);
        try
        {
            var content = From<WebSiteResourceHandler>(self).content;
            var read = content?.Read(new Span<byte>(dataOut, bytesToRead)) ?? 0;
            *bytesRead = read;
            return read > 0 ? 1 : 0;
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
            *bytesRead = -2;
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static void Cancel(CefResourceHandler* self)
    {
        try
        {
            From<WebSiteResourceHandler>(self).Close();
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }
}
