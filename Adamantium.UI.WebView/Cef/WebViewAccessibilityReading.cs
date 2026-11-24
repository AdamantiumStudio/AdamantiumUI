using System.Buffers;
using System.Text;
using System.Threading.Channels;
using Adamantium.Mathematics;
using Adamantium.UI.Core;
using Adamantium.UI.WebView.Accessibility;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// Reads what Chromium says of the pages' accessibility trees off the UI loop, for all the engine's browsers: a heavy
/// page's first tree is megabytes, its JSON written and read in half a second (measured 24.11) - the loop would stall for
/// it. CEF's values are for any thread; each is kept from its callback, written and read here in the order it came, let
/// go of, and what was read handed to its browser on the loop.
/// </summary>
internal sealed class WebViewAccessibilityReading : IDisposable
{
    private readonly Channel<Said> waiting =
        Channel.CreateUnbounded<Said>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Task reading;

    public WebViewAccessibilityReading()
    {
        reading = Task.Run(ReadAll);
    }

    /// <summary>Keeps <paramref name="value"/>, which CEF lends for its callback, to read it for
    /// <paramref name="browser"/>: a change of a tree, or nodes that moved.</summary>
    public unsafe void Read(WebViewBrowser browser, CefValue* value, bool isTreeChange)
    {
        if (value == null)
        {
            return;
        }

        CefRefCounting.AddRefCef(value);
        if (!waiting.Writer.TryWrite(new Said(browser, (IntPtr)value, isTreeChange, default)))
        {
            CefRefCounting.ReleaseCef(value);
        }
    }

    /// <summary>Hands the document's scroll back to <paramref name="browser"/> after what was said before it: a new
    /// document read later would forget a scroll taken in before it.</summary>
    public void Scrolled(WebViewBrowser browser, Vector2<double> scroll) =>
        waiting.Writer.TryWrite(new Said(browser, IntPtr.Zero, false, scroll));

    /// <summary>Stops reading and waits for what is being read: CEF is to be shut down only once its values are let go.
    /// What still waits is let go of unread - its browsers are closing by now.</summary>
    public void Dispose()
    {
        waiting.Writer.TryComplete();
        reading.Wait();
    }

    // Taken in on the loop; what goes wrong there is told as any CEF callback's is.
    private static void Hand(Action take) =>
        LoopSignal.Post(() =>
        {
            try
            {
                take();
            }
            catch (Exception exception)
            {
                CefCallbacks.Report(exception);
            }
        });

    private async Task ReadAll()
    {
        await foreach (var said in waiting.Reader.ReadAllAsync())
        {
            if (said.Value == IntPtr.Zero)
            {
                Hand(() => said.Browser.TakeScroll(said.Scroll));
            }
            else
            {
                ReadOne(said);
            }
        }
    }

    private static unsafe void ReadOne(Said said)
    {
        var value = (CefValue*)said.Value;
        try
        {
            // A closing page's late word is not read: let go of as it comes.
            if (said.Browser.IsClosing || Utf8Json(value, out var length) is not { } json)
            {
                return;
            }

            try
            {
                var span = json.AsSpan(0, length);
                if (said.IsTreeChange)
                {
                    var change = WebViewAccessibilityReader.ReadTreeChange(span);
                    Hand(() => said.Browser.TakeAccessibility(change));
                }
                else
                {
                    var moves = WebViewAccessibilityReader.ReadMoves(span);
                    Hand(() => said.Browser.TakeAccessibility(moves));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(json);
            }
        }
        catch (Exception exception)
        {
            CefCallbacks.Report(exception);
        }
        finally
        {
            CefRefCounting.ReleaseCef(value);
        }
    }

    // The value written by CEF as JSON, in UTF-8, in a buffer from the shared pool; null for none.
    private static unsafe byte[] Utf8Json(CefValue* value, out int length)
    {
        length = 0;
        var written = (CefStringUtf16*)CefInterop.CefWriteJson(value, CefJsonWriterOptions.Default).pointer;
        if (written == null)
        {
            return null;
        }

        try
        {
            var chars = new ReadOnlySpan<char>(written->str, (int)written->length);
            var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, Encoding.UTF8.GetByteCount(chars)));
            length = Encoding.UTF8.GetBytes(chars, buffer);
            return buffer;
        }
        finally
        {
            CefInterop.CefStringUserfreeUtf16Free(new CefStringUserfreeUtf16 { pointer = written });
        }
    }

    // A value of CEF's to read, or - with none - the document's scroll to hand back.
    private readonly record struct Said(WebViewBrowser Browser, IntPtr Value, bool IsTreeChange, Vector2<double> Scroll);
}
