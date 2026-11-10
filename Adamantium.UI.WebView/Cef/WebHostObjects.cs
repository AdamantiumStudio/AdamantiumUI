using System.Text.Json;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The objects of the application's a bridge lets pages call: each under its name, for the origins it was added for,
/// in their main frame alone; and the objects calls answered with, each held for the script world it was given to
/// until the page lets go of it, its document goes, or the object it came from is taken away. A call of anything else
/// is refused alike - a page learns nothing of what it may not reach. Used on the UI loop.
/// </summary>
internal sealed class WebHostObjects
{
    private readonly Dictionary<string, WebHostObjectEntry> named = new(StringComparer.Ordinal);
    private readonly Dictionary<(WebViewBrowser Browser, int Context, string Handle), WebHostObjectEntry> given = [];
    private int nextHandle;

    public void Add(string name, WebHostObject hostObject, JsonSerializerOptions options, IEnumerable<string> origins)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(hostObject);
        ArgumentNullException.ThrowIfNull(origins);
        if (name.StartsWith('#'))
        {
            throw new ArgumentException("A name does not start with '#'.", nameof(name));
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var origin in origins)
        {
            allowed.Add(Origin(origin));
        }

        if (allowed.Count == 0)
        {
            throw new ArgumentException("An object is let in for the origins named, and none was.", nameof(origins));
        }

        Remove(name);
        named[name] = new WebHostObjectEntry(hostObject, options ?? JsonSerializerOptions.Web, allowed, name);
    }

    public bool Remove(string name)
    {
        if (!named.Remove(name))
        {
            return false;
        }

        List<(WebViewBrowser, int, string)> derived = null;
        foreach (var (key, entry) in given)
        {
            if (entry.Root == name)
            {
                (derived ??= []).Add(key);
            }
        }

        if (derived != null)
        {
            foreach (var key in derived)
            {
                given.Remove(key);
            }
        }

        return true;
    }

    /// <summary>A script world of <paramref name="browser"/> is gone: what it was given goes with it.</summary>
    public void ContextGone(WebViewBrowser browser, int context) =>
        Forget(key => ReferenceEquals(key.Browser, browser) && key.Context == context);

    /// <summary>The bridge's page is another's now: nothing given is held.</summary>
    public void ForgetGiven() => given.Clear();

    public void Call(WebHostCall call)
    {
        int id;
        string target, method;
        JsonElement arguments;
        using (var document = JsonDocument.Parse(call.Json))
        {
            var root = document.RootElement;
            if (root.TryGetProperty("release", out var released))
            {
                given.Remove((call.Browser, call.Context, released.GetString()));
                return;
            }

            if (!root.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out id))
            {
                return;
            }

            target = root.TryGetProperty("target", out var t) ? t.GetString() : null;
            method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
            arguments = root.TryGetProperty("args", out var a) ? a.Clone() : default;
        }

        WebHostObjectEntry entry = null;
        if (target != null && !(target.StartsWith('#')
                ? given.TryGetValue((call.Browser, call.Context, target), out entry)
                : named.TryGetValue(target, out entry)))
        {
            entry = null;
        }

        if (entry == null || !call.IsMainFrame || !entry.Origins.Contains(call.Origin ?? string.Empty))
        {
            call.Refuse(id, $"There is no object '{target}' here.");
            return;
        }

        _ = Answer(call, id, entry, method, arguments);
    }

    private async Task Answer(WebHostCall call, int id, WebHostObjectEntry entry, string method, JsonElement arguments)
    {
        WebHostAnswer answer;
        try
        {
            var options = entry.Options;
            answer = await entry.Object.InvokeAsync(method,
                new WebHostArguments(arguments, options, number => new WebScriptFunction(call.Browser, call.Context, number, options)));
        }
        catch (Exception exception)
        {
            call.Refuse(id, exception.Message);
            return;
        }

        // The method may have gone on off the loop; what the page is given is held, and told, on it.
        OnLoop(() =>
        {
            string handle = null;
            if (answer.Object != null)
            {
                handle = "#" + ++nextHandle;
                given[(call.Browser, call.Context, handle)] = entry with { Object = answer.Object };
            }

            call.Answer(id, answer, handle);
        });
    }

    private static void OnLoop(Action action)
    {
        if (LoopSignal.IsLoopThread)
        {
            action();
        }
        else
        {
            LoopSignal.Post(action);
        }
    }

    private void Forget(Func<(WebViewBrowser Browser, int Context, string Handle), bool> gone)
    {
        List<(WebViewBrowser, int, string)> keys = null;
        foreach (var key in given.Keys)
        {
            if (gone(key))
            {
                (keys ??= []).Add(key);
            }
        }

        if (keys != null)
        {
            foreach (var key in keys)
            {
                given.Remove(key);
            }
        }
    }

    // An origin as Chromium names a document's: scheme, host and a port other than the scheme's own.
    private static string Origin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException($"'{origin}' is no origin a page can have.", nameof(origin));
        }

        return uri.Scheme + "://" + uri.Authority;
    }
}
