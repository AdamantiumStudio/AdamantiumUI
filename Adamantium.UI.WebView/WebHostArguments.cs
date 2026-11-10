using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Adamantium.UI.WebView.Cef;

namespace Adamantium.UI.WebView;

/// <summary>
/// The arguments of a page's call of an object of the application's, read as the method takes them, and its answer
/// written: JSON both ways, by the <see cref="JsonSerializerOptions"/> the object was added with - their type
/// information resolver for an application compiled ahead of time.
/// </summary>
public sealed class WebHostArguments
{
    private readonly JsonElement arguments;
    private readonly JsonSerializerOptions options;
    private readonly Func<int, IWebScriptFunction> function;

    internal WebHostArguments(JsonElement arguments, JsonSerializerOptions options, Func<int, IWebScriptFunction> function)
    {
        this.arguments = arguments;
        this.options = options;
        this.function = function;
    }

    /// <summary>How many arguments the page passed.</summary>
    public int Count => arguments.ValueKind == JsonValueKind.Array ? arguments.GetArrayLength() : 0;

    /// <summary>The argument at <paramref name="index"/> as a <typeparamref name="T"/>; the default for one the page
    /// did not pass, or passed as <c>null</c>.</summary>
    public T Get<T>(int index)
    {
        if (index >= Count || arguments[index].ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        return arguments[index].Deserialize((JsonTypeInfo<T>)options.GetTypeInfo(typeof(T)));
    }

    /// <summary>The function the page passed at <paramref name="index"/>; null for none. Throws for another value.
    /// </summary>
    public IWebScriptFunction Function(int index)
    {
        if (index >= Count || arguments[index].ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (arguments[index].ValueKind != JsonValueKind.Object ||
            !arguments[index].TryGetProperty(WebViewDevTools.FunctionKey, out var id) || !id.TryGetInt32(out var number))
        {
            throw new ArgumentException($"Argument {index} is to be a function.");
        }

        return function(number);
    }

    /// <summary>The answer that is <paramref name="value"/>, written as JSON.</summary>
    public WebHostAnswer Answer<T>(T value) =>
        WebHostAnswer.FromJson(JsonSerializer.Serialize(value, (JsonTypeInfo<T>)options.GetTypeInfo(typeof(T))));
}
