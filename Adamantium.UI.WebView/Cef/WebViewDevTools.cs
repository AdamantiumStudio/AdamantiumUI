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

    /// <summary>Stops: what was asked and not answered fails, the browser hears nothing more.</summary>
    public unsafe void Detach()
    {
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

    internal void OnMessage(ReadOnlySpan<byte> message)
    {
        using var document = JsonDocument.Parse(message.ToArray());
        var root = document.RootElement;
        if (root.TryGetProperty("id", out var id) && pending.Remove(id.GetInt32(), out var call))
        {
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

            return;
        }

        if (root.TryGetProperty("method", out var method) && method.GetString() == "Runtime.bindingCalled" &&
            root.TryGetProperty("params", out var parameters) &&
            parameters.GetProperty("name").GetString() == Binding)
        {
            messageReceived(parameters.GetProperty("payload").GetString());
        }
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
