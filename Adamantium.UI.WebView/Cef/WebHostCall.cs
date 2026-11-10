using System.Text.Json;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Cef;

/// <summary>A page's call of an object of the application's as it came: from which browser and script world, whether
/// in the main frame and at which origin, the call itself as JSON - and the way back to answer it.</summary>
internal sealed class WebHostCall
{
    public WebHostCall(WebViewBrowser browser, string context, bool isMainFrame, string origin, string json)
    {
        Browser = browser;
        Context = context;
        IsMainFrame = isMainFrame;
        Origin = origin;
        Json = json;
    }

    /// <summary>The browser the page is in.</summary>
    public WebViewBrowser Browser { get; }

    /// <summary>The script world the call came from, and is answered into, by its unique id.</summary>
    public string Context { get; }

    /// <summary>Whether the call came from the page's main frame.</summary>
    public bool IsMainFrame { get; }

    /// <summary>The origin of the document that called.</summary>
    public string Origin { get; }

    /// <summary>The call: <c>{id, target, method, args}</c>, or <c>{release}</c> for an object the page let go of.
    /// </summary>
    public string Json { get; }

    /// <summary>Answers the call <paramref name="id"/> with <paramref name="answer"/> - the object given under
    /// <paramref name="handle"/> - on the UI loop, telling the page which of the functions it passed were not taken,
    /// for it to let go of. An answer that is no JSON refuses the call instead.</summary>
    public void Answer(int id, WebHostAnswer answer, string handle, IReadOnlyList<int> untaken)
    {
        var reply = new System.Buffers.ArrayBufferWriter<byte>();
        try
        {
            using var writer = new Utf8JsonWriter(reply);
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

            if (untaken != null)
            {
                writer.WriteStartArray("letGo");
                foreach (var number in untaken)
                {
                    writer.WriteNumberValue(number);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            Refuse(id, "The answer is no JSON.");
            return;
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
