using System.Runtime.InteropServices;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>An answer the application gives a request itself - a file of a site of its own, or what a handler of the
/// page's requests answered: the content read from its start as Chromium asks for it and closed once read, with its
/// type, status and headers; or a redirect. Called on CEF's IO thread.</summary>
internal sealed unsafe class WebViewResourceAnswer : CefObject
{
    private readonly string mimeType;
    private readonly int status;
    private readonly IReadOnlyDictionary<string, string> headers;
    private readonly string redirectTo;
    private Stream content;

    private WebViewResourceAnswer(Stream content, string mimeType, int status, IReadOnlyDictionary<string, string> headers,
        string redirectTo) : base(sizeof(CefResourceHandler))
    {
        if (content is { CanSeek: true })
        {
            content.Position = 0;
        }

        this.content = content;
        this.mimeType = mimeType;
        this.status = status;
        this.headers = headers;
        this.redirectTo = redirectTo;
        var handler = (CefResourceHandler*)Native;
        handler->open = &Open;
        handler->get_response_headers = &GetResponseHeaders;
        handler->skip = &Skip;
        handler->read = &Read;
        handler->cancel = &Cancel;
    }

    /// <summary>The answer, for CEF to hold: the only reference to it is the one CEF takes.</summary>
    public static CefResourceHandler* Make(Stream content, string mimeType, int status,
        IReadOnlyDictionary<string, string> headers = null)
    {
        var answer = new WebViewResourceAnswer(content, mimeType, status, headers, null);
        var shared = (CefResourceHandler*)answer.Share();
        answer.Release();
        return shared;
    }

    /// <summary>An answer that sends the request to <paramref name="address"/>, for CEF to hold.</summary>
    public static CefResourceHandler* MakeRedirect(string address)
    {
        var answer = new WebViewResourceAnswer(null, "text/plain", 302, null, address);
        var shared = (CefResourceHandler*)answer.Share();
        answer.Release();
        return shared;
    }

    protected override void OnFreed() => Close();

    private void Close()
    {
        Interlocked.Exchange(ref content, null)?.Dispose();
    }

    [UnmanagedCallersOnly]
    private static int Open(CefResourceHandler* self, CefRequest* request, int* handleRequest, CefCallback* callback)
    {
        // The content is at hand: answered at once, nothing to wait for.
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
            var handler = From<WebViewResourceAnswer>(self);
            var content = handler.content;
            if (handler.redirectTo != null)
            {
                CefStrings.Set(redirectUrl, handler.redirectTo);
            }

            Set(response, handler.status, handler.mimeType, handler.headers);
            *responseLength = content == null ? 0 : content.CanSeek ? content.Length - content.Position : -1;
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }

        CefRefCounting.ReleaseCef(response);
    }

    private static void Set(CefResponse* response, int status, string mimeType, IReadOnlyDictionary<string, string> headers)
    {
        var type = new CefStringUtf16();
        var text = new CefStringUtf16();
        try
        {
            CefStrings.Set(&type, mimeType);
            CefStrings.Set(&text, status switch
            {
                200 => "OK", 302 => "Found", 404 => "Not Found", 500 => "Internal Server Error", _ => string.Empty
            });
            response->set_status(response, status);
            response->set_status_text(response, &text);
            response->set_mime_type(response, &type);
        }
        finally
        {
            CefStrings.Clear(&type);
            CefStrings.Clear(&text);
        }

        if (headers == null)
        {
            return;
        }

        foreach (var (name, value) in headers)
        {
            var cefName = new CefStringUtf16();
            var cefValue = new CefStringUtf16();
            try
            {
                CefStrings.Set(&cefName, name);
                CefStrings.Set(&cefValue, value);
                response->set_header_by_name(response, &cefName, &cefValue, 1);
            }
            finally
            {
                CefStrings.Clear(&cefName);
                CefStrings.Clear(&cefValue);
            }
        }
    }

    [UnmanagedCallersOnly]
    private static int Skip(CefResourceHandler* self, long bytesToSkip, long* bytesSkipped, CefResourceSkipCallback* callback)
    {
        CefRefCounting.ReleaseCef(callback);
        try
        {
            var content = From<WebViewResourceAnswer>(self).content;
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
            var handler = From<WebViewResourceAnswer>(self);
            var read = handler.content?.Read(new Span<byte>(dataOut, bytesToRead)) ?? 0;
            *bytesRead = read;
            if (read == 0)
            {
                // All read: the file is let go of now, not when CEF lets go of the answer.
                handler.Close();
            }

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
            From<WebViewResourceAnswer>(self).Close();
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
    }
}
