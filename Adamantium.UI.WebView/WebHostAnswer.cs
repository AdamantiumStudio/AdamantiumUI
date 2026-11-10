namespace Adamantium.UI.WebView;

/// <summary>What a call of an object of the application's answers the page with: nothing, a value as JSON, or another
/// object the page calls in turn.</summary>
public readonly struct WebHostAnswer
{
    private WebHostAnswer(string json, WebHostObject hostObject)
    {
        Json = json;
        Object = hostObject;
    }

    /// <summary>No value: the page gets <c>undefined</c>.</summary>
    public static WebHostAnswer None => default;

    /// <summary>The value, as JSON; null for none or for an object.</summary>
    public string Json { get; }

    /// <summary>The object the page gets to call; null for a value.</summary>
    public WebHostObject Object { get; }

    /// <summary>A value the page gets, written as <paramref name="json"/>.</summary>
    public static WebHostAnswer FromJson(string json) => new(json ?? "null", null);

    /// <summary>An object the page gets to call; none answers <c>null</c>.</summary>
    public static WebHostAnswer FromObject(WebHostObject hostObject) =>
        hostObject == null ? FromJson("null") : new(null, hostObject);
}
