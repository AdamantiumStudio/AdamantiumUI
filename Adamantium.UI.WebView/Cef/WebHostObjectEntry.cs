using System.Text.Json;

namespace Adamantium.UI.WebView.Cef;

/// <summary>An object of the application's a page may call: how its calls are written, the origins let in, and the
/// name of the object added to the bridge it came from.</summary>
internal sealed record WebHostObjectEntry(WebHostObject Object, JsonSerializerOptions Options, HashSet<string> Origins,
    string Root);
