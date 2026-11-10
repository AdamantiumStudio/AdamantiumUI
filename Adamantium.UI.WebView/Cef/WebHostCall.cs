using System.Text.Json;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A page's call of an object of the application's as it came: from which browser and script world, whether
/// in the main frame and at which origin, the call itself as JSON - and the way back to answer it.</summary>
internal sealed class WebHostCall
{
    public WebHostCall(WebViewBrowser browser, int context, bool isMainFrame, string origin, string json)
    {
        Browser = browser;
        Context = context;
        IsMainFrame = isMainFrame;
        Origin = origin;
        Json = json;
    }

    /// <summary>The browser the page is in.</summary>
    public WebViewBrowser Browser { get; }

    /// <summary>The script world the call came from, and is answered into.</summary>
    public int Context { get; }

    /// <summary>Whether the call came from the page's main frame.</summary>
    public bool IsMainFrame { get; }

    /// <summary>The origin of the document that called.</summary>
    public string Origin { get; }

    /// <summary>The call: <c>{id, target, method, args}</c>, or <c>{release}</c> for an object the page let go of.
    /// </summary>
    public string Json { get; }

    /// <summary>Answers the call <paramref name="id"/> with <paramref name="answer"/>, on the UI loop.</summary>
    public void Answer(int id, WebHostAnswer answer, string handle)
    {
        var reply = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(reply))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            if (handle != null)
            {
                writer.WriteString("object", handle);
            }
            else if (answer.Json != null)
            {
                writer.WritePropertyName("value");
                writer.WriteRawValue(answer.Json);
            }

            writer.WriteEndObject();
        }

        Send(System.Text.Encoding.UTF8.GetString(reply.WrittenSpan));
    }

    /// <summary>Refuses the call with <paramref name="message"/>, read for its id; nothing for an object let go of.
    /// </summary>
    public void Refuse(string message)
    {
        using var document = JsonDocument.Parse(Json);
        if (document.RootElement.TryGetProperty("id", out var id) && id.TryGetInt32(out var number))
        {
            Refuse(number, message);
        }
    }

    /// <summary>Refuses the call <paramref name="id"/> with <paramref name="message"/>, on the UI loop.</summary>
    public void Refuse(int id, string message)
    {
        var reply = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(reply))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", id);
            writer.WriteString("error", message);
            writer.WriteEndObject();
        }

        Send(System.Text.Encoding.UTF8.GetString(reply.WrittenSpan));
    }

    private void Send(string json)
    {
        if (LoopSignal.IsLoopThread)
        {
            Browser.AnswerHost(Context, json);
        }
        else
        {
            LoopSignal.Post(() => Browser.AnswerHost(Context, json));
        }
    }
}
