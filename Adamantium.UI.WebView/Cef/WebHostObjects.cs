using System.Text.Json;
using Adamantium.UI.Core;

namespace Adamantium.UI.WebView.Cef;

/// <summary>
/// The objects of the application's a bridge lets pages call: each under its name, for the origins it was added for,
/// in their main frame alone; and the objects calls answered with, each held for the script world it was given to
/// until the page lets go of it, its document goes, or the object it came from is taken away - a call still running
/// then gives nothing. A call of anything else is refused alike - a page learns nothing of what it may not reach.
/// Used on the UI loop.
/// </summary>
internal sealed class WebHostObjects
{
    private readonly Dictionary<string, WebHostObjectEntry> named = new(StringComparer.Ordinal);
    private readonly Dictionary<(WebViewBrowser Browser, string Context, string Handle), WebHostObjectEntry> given = [];
    private int nextHandle;
    private int pageChanges;

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
        named[name] = new WebHostObjectEntry(hostObject, options ?? JsonSerializerOptions.Web, allowed, name, hostObject);
    }

    public bool Remove(string name)
    {
        if (!named.Remove(name))
        {
            return false;
        }

        Forget(key => given[key].Root == name);
        return true;
    }

    /// <summary>A script world of <paramref name="browser"/> is gone: what it was given goes with it.</summary>
    public void ContextGone(WebViewBrowser browser, string context) =>
        Forget(key => ReferenceEquals(key.Browser, browser) && key.Context == context);

    /// <summary>The bridge's page is another's now: nothing given is held, nor given by a call still running.</summary>
    public void ForgetGiven()
    {
        given.Clear();
        pageChanges++;
    }

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
        var page = pageChanges;
        var options = entry.Options;
        var read = new WebHostArguments(arguments, options,
            number => new WebScriptFunction(call.Browser, call.Context, number, options));
        WebHostAnswer answer;
        try
        {
            answer = await entry.Object.InvokeAsync(method, read);
        }
        catch (Exception exception)
        {
            call.Refuse(id, exception.Message);
            return;
        }

        // The method may have gone on off the loop; what the page is given is held, and told, on it - unless the page
        // let go of the world it called from, or the object it came from was taken away, meanwhile.
        OnLoop(() =>
        {
            if (!call.Browser.HasContext(call.Context))
            {
                return;
            }

            string handle = null;
            if (answer.Object != null)
            {
                if (page != pageChanges || !named.TryGetValue(entry.Root, out var root) ||
                    !ReferenceEquals(root.Added, entry.Added))
                {
                    call.Refuse(id, "The object is gone.");
                    return;
                }

                handle = "#" + ++nextHandle;
                given[(call.Browser, call.Context, handle)] = entry with { Object = answer.Object };
            }

            call.Answer(id, answer, handle, read.Untaken());
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

    private void Forget(Func<(WebViewBrowser Browser, string Context, string Handle), bool> gone)
    {
        List<(WebViewBrowser, string, string)> keys = null;
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

    // An origin as Chromium names a document's: scheme, host - a name in punycode - and a port other than the scheme's
    // own.
    private static string Origin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new ArgumentException($"'{origin}' is no origin a page can have.", nameof(origin));
        }

        var host = uri.HostNameType == UriHostNameType.Dns ? uri.IdnHost : uri.Host;
        return uri.Scheme + "://" + host + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port);
    }
}
