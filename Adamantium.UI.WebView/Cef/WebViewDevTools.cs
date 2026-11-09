using System.Buffers;
using System.Text.Json;
using Adamantium.UI.WebView.Cef.Interop;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// A browser's own DevTools protocol, spoken to run a script with its answer and to give its pages
/// <c>window.adamantium</c>: a binding the page's messages arrive through, and a script on every new document that
/// wraps it with <c>postMessage</c> and message listeners. Lives on the UI loop, where CEF calls back.
/// </summary>
internal sealed class WebViewDevTools
{
    private const string Binding = "__adamantiumPost";

    private const string PageScript =
        "(()=>{if(window.adamantium)return;const listeners=[];window.adamantium={" +
        "postMessage:m=>" + Binding + "(JSON.stringify(m))," +
        "addEventListener:(t,f)=>{if(t==='message')listeners.push(f)}," +
        "removeEventListener:(t,f)=>{const i=listeners.indexOf(f);if(i>=0)listeners.splice(i,1)}," +
        "__receive:j=>{const data=JSON.parse(j);for(const f of listeners.slice())f({data})}};})()";

    private readonly Action<string> messageReceived;
    private readonly Dictionary<int, TaskCompletionSource<JsonElement>> pending = [];
    private readonly Dictionary<int, (byte[] Id, bool EnablesOverlay)> forwarded = [];
    private Action<string> frontEnd;
    private WebViewDevToolsObserver observer;
    private unsafe CefRegistration* registration;
    private int nextId;

    public WebViewDevTools(Action<string> messageReceived)
    {
        this.messageReceived = messageReceived;
    }

    /// <summary>Starts speaking to the browser behind <paramref name="host"/>: its runtime, the page's binding and
    /// script, for the documents to come and the one there now.</summary>
    public unsafe void Attach(CefBrowserHost* host)
    {
        observer = new WebViewDevToolsObserver(this);
        registration = host->add_dev_tools_message_observer(host, observer.Shared);
        Send(host, "Runtime.enable", null);
        Send(host, "Page.enable", null);
        Send(host, "Runtime.addBinding", writer => writer.WriteString("name", Binding));
        Send(host, "Page.addScriptToEvaluateOnNewDocument", writer => writer.WriteString("source", PageScript));
        Prepare(host);
    }

    /// <summary>Gives the page there now its <c>window.adamantium</c>, when the script for new documents came too late
    /// for it; a page that has it keeps it.</summary>
    public unsafe void Prepare(CefBrowserHost* host) =>
        Send(host, "Runtime.evaluate", writer => writer.WriteString("expression", PageScript));

    /// <summary>Runs <paramref name="script"/> in the page; its value as JSON, or the page's message as a
    /// <see cref="WebViewScriptException"/>.</summary>
    public unsafe Task<string> EvaluateAsync(CefBrowserHost* host, string script) =>
        ValueOf(Call(host, "Runtime.evaluate", writer =>
        {
            writer.WriteString("expression", script);
            writer.WriteBoolean("returnByValue", true);
            writer.WriteBoolean("awaitPromise", true);
        }));

    private static async Task<string> ValueOf(Task<JsonElement> call)
    {
        var answer = await call;
        if (answer.TryGetProperty("exceptionDetails", out var failure))
        {
            var text = failure.TryGetProperty("exception", out var exception) &&
                       exception.TryGetProperty("description", out var description)
                ? description.GetString()
                : failure.TryGetProperty("text", out var plain) ? plain.GetString() : "The script threw.";
            throw new WebViewScriptException(text);
        }

        return answer.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value)
            ? value.GetRawText()
            : "null";
    }

    /// <summary>Hands the page <paramref name="json"/>: its message listeners get it parsed.</summary>
    public unsafe void Post(CefBrowserHost* host, string json) =>
        Send(host, "Runtime.evaluate",
            writer => writer.WriteString("expression",
                "window.adamantium&&window.adamantium.__receive(" + Quoted(json) + ")"));

    /// <summary>Speaks for a DevTools front end too: what <see cref="Forward"/> hands on is answered to
    /// <paramref name="send"/>, and every event the page tells goes there as well. Null lets it go, and the page's own
    /// runtime, page and binding are asked for again - the front end may have turned them off.</summary>
    public unsafe void ServeFrontEnd(CefBrowserHost* host, Action<string> send)
    {
        frontEnd = send;
        FrontEndHearsOverlay = false;
        if (send == null)
        {
            FrontEndGone(host);
        }
    }

    /// <summary>A front end went, or another took its place: its calls are forgotten - their answers would reach the
    /// next under the same ids - and what it may have left the page in is undone: a debugger paused at a breakpoint,
    /// the inspect overlay, a device emulated, new targets held for a debugger, the cache off. The page's own runtime,
    /// page and binding are asked for again.</summary>
    public unsafe void FrontEndGone(CefBrowserHost* host)
    {
        forwarded.Clear();
        FrontEndHearsOverlay = false;
        if (host == null)
        {
            return;
        }

        Send(host, "Debugger.disable", null);
        Send(host, "Overlay.disable", null);
        Send(host, "Emulation.clearDeviceMetricsOverride", null);
        Send(host, "Target.setAutoAttach", writer =>
        {
            writer.WriteBoolean("autoAttach", false);
            writer.WriteBoolean("waitForDebuggerOnStart", false);
        });
        Send(host, "Network.setCacheDisabled", writer => writer.WriteBoolean("cacheDisabled", false));
        Send(host, "Runtime.enable", null);
        Send(host, "Page.enable", null);
        Send(host, "Runtime.addBinding", writer => writer.WriteString("name", Binding));
    }

    /// <summary>Hands the page a call of the DevTools front end's, as JSON: under an id of ours, so it never meets one of
    /// our own calls, and answered under the front end's.</summary>
    public unsafe void Forward(CefBrowserHost* host, string json)
    {
        var call = System.Text.Encoding.UTF8.GetBytes(json);
        if (frontEnd == null || !TryReadTop(call, out var id, out var method) || id.Start < 0)
        {
            return;
        }

        var ours = ++nextId;
        forwarded[ours] = (call[id.Start..id.End], method == "Overlay.enable");
        var bytes = WithId(call, id, System.Text.Encoding.UTF8.GetBytes(ours.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        fixed (byte* message = bytes)
        {
            host->send_dev_tools_message(host, message, (ulong)bytes.Length);
        }
    }

    /// <summary>Whether the front end served now listens to the page's overlay - it has enabled it, and an element shown
    /// to it the way its inspect mode shows one opens in its Elements panel, whatever panel it shows.</summary>
    public bool FrontEndHearsOverlay { get; private set; }

    /// <summary>Raised on the UI loop once the front end listens to the overlay.</summary>
    public Action OverlayHeard { get; set; }

    /// <summary>The element at <paramref name="x"/>, <paramref name="y"/> of the page, in its CSS pixels, as the page is
    /// laid out now - before the DevTools opening beside it lay it out again.</summary>
    public unsafe Task<JsonElement> Locate(CefBrowserHost* host, double x, double y) =>
        Call(host, "DOM.getNodeForLocation", writer =>
        {
            writer.WriteNumber("x", (int)Math.Round(x));
            writer.WriteNumber("y", (int)Math.Round(y));
            writer.WriteBoolean("ignorePointerEventsNone", true);
        });

    /// <summary>Shows the front end served now the element <paramref name="located"/> found, as its own inspect mode
    /// would: the Elements panel opens on it.</summary>
    public void Reveal(Task<JsonElement> located)
    {
        var send = frontEnd;
        if (send == null)
        {
            return;
        }

        located.ContinueWith(answer =>
        {
            // Nothing there, or the page gone before it answered: nothing to show, and the failure is seen here.
            _ = answer.Exception;
            if (answer.IsCompletedSuccessfully && answer.Result.TryGetProperty("backendNodeId", out var node))
            {
                send("{\"method\":\"Overlay.inspectNodeRequested\",\"params\":{\"backendNodeId\":" +
                     node.GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}");
            }
        }, TaskScheduler.Default);
    }

    /// <summary>Stops: what was asked and not answered fails, the browser hears nothing more.</summary>
    public unsafe void Detach()
    {
        frontEnd = null;
        forwarded.Clear();
        foreach (var call in pending.Values)
        {
            call.TrySetException(new WebViewScriptException("The page went away before it answered."));
        }

        pending.Clear();
        if (registration != null)
        {
            CefRefCounting.ReleaseCef(registration);
            registration = null;
        }

        observer?.Release();
        observer = null;
    }

    /// <summary>Forgets the browser CEF did not close before it shut down: what was asked fails, nothing is released
    /// to a CEF that is gone.</summary>
    public unsafe void Abandon()
    {
        foreach (var call in pending.Values)
        {
            call.TrySetException(new WebViewScriptException("The page went away before it answered."));
        }

        pending.Clear();
        registration = null;
        observer = null;
    }

    // A message of the page's is read only as deep as its top - its id and method - however deep the rest goes: a
    // real site's document tree nests far past a JSON reader's usual depth, and a message lost to it leaves the front
    // end waiting for an answer forever. Only our own answers and the page's binding calls, both shallow, are read
    // whole.
    internal void OnMessage(ReadOnlySpan<byte> message)
    {
        if (!TryReadTop(message, out var idRange, out var method))
        {
            return;
        }

        if (idRange.Start < 0)
        {
            frontEnd?.Invoke(System.Text.Encoding.UTF8.GetString(message));
            if (method == "Runtime.bindingCalled")
            {
                OnBindingCalled(message);
            }

            return;
        }

        if (!int.TryParse(message[idRange.Start..idRange.End], out var id))
        {
            return;
        }

        if (forwarded.Remove(id, out var theirs))
        {
            frontEnd?.Invoke(System.Text.Encoding.UTF8.GetString(WithId(message, idRange, theirs.Id)));
            if (theirs.EnablesOverlay && frontEnd != null && message.IndexOf("\"error\""u8) < 0)
            {
                FrontEndHearsOverlay = true;
                OverlayHeard?.Invoke();
            }

            return;
        }

        if (pending.Remove(id, out var call))
        {
            using var document = JsonDocument.Parse(message.ToArray(), Whole);
            var root = document.RootElement;
            if (root.TryGetProperty("result", out var result))
            {
                call.TrySetResult(result.Clone());
            }
            else
            {
                var error = root.TryGetProperty("error", out var failure) && failure.TryGetProperty("message", out var text)
                    ? text.GetString()
                    : "The page did not answer.";
                call.TrySetException(new WebViewScriptException(error));
            }
        }
    }

    private static readonly JsonDocumentOptions Whole = new() { MaxDepth = 256 };

    private void OnBindingCalled(ReadOnlySpan<byte> message)
    {
        using var document = JsonDocument.Parse(message.ToArray(), Whole);
        if (document.RootElement.TryGetProperty("params", out var parameters) &&
            parameters.TryGetProperty("name", out var name) && name.GetString() == Binding &&
            parameters.TryGetProperty("payload", out var payload))
        {
            messageReceived(payload.GetString());
        }
    }

    // The top of a protocol message: where its id's value stands (Start -1 for none) and its method, the rest skipped
    // unread whatever its depth. False for what is not an object.
    private static bool TryReadTop(ReadOnlySpan<byte> json, out (int Start, int End) id, out string method)
    {
        id = (-1, -1);
        method = null;
        var reader = new Utf8JsonReader(json, new JsonReaderOptions { MaxDepth = int.MaxValue });
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                return false;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isId = reader.ValueTextEquals("id"u8);
                var isMethod = !isId && reader.ValueTextEquals("method"u8);
                reader.Read();
                if (isId && reader.TokenType is JsonTokenType.Number or JsonTokenType.String)
                {
                    var start = (int)reader.TokenStartIndex;
                    id = (start, (int)reader.BytesConsumed);
                }
                else if (isMethod && reader.TokenType == JsonTokenType.String)
                {
                    method = reader.GetString();
                }
                else
                {
                    reader.Skip();
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The message with the value at id put in place of its own: the raw JSON of a number or a string.
    private static byte[] WithId(ReadOnlySpan<byte> message, (int Start, int End) id, ReadOnlySpan<byte> value)
    {
        var bytes = new byte[message.Length - (id.End - id.Start) + value.Length];
        message[..id.Start].CopyTo(bytes);
        value.CopyTo(bytes.AsSpan(id.Start));
        message[id.End..].CopyTo(bytes.AsSpan(id.Start + value.Length));
        return bytes;
    }

    private static string Quoted(string text)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStringValue(text);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private unsafe Task<JsonElement> Call(CefBrowserHost* host, string method, Action<Utf8JsonWriter> parameters)
    {
        var call = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[Send(host, method, parameters)] = call;
        return call.Task;
    }

    private unsafe int Send(CefBrowserHost* host, string method, Action<Utf8JsonWriter> parameters)
    {
        var id = ++nextId;
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("method", method);
            writer.WriteStartObject("params");
            parameters?.Invoke(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        fixed (byte* bytes = buffer.WrittenSpan)
        {
            host->send_dev_tools_message(host, bytes, (ulong)buffer.WrittenCount);
        }

        return id;
    }
}
